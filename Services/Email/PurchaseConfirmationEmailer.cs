using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Services.Helpers;
using Services.Repositories.Interfaces;

namespace Services.Email
{
    /// <summary>
    /// The "here is your QR" confirmation for a single purchased thing — a day pass, an admission,
    /// a season pass.
    ///
    /// Extracted from StripePurchaseFinalizer, which owned the only copy. That was fine while every
    /// one of these purchases settled through Stripe, but the gate counter can settle a sale in cash
    /// and never touch a webhook, so a cash season pass would have gone out with no email at all and
    /// no QR for the rider to show. Rather than keep a second copy of the template in sync by hand,
    /// both callers now share this one.
    ///
    /// Distinct from <see cref="IEventOrderConfirmationEmailer"/>, which confirms a whole EVENT ORDER
    /// (tickets plus the add-ons attached to them). This confirms one item that stands on its own.
    /// </summary>
    public interface IPurchaseConfirmationEmailer
    {
        /// <param name="kind">"pass" | "event_ticket" | "season_pass"; anything else gets generic wording.</param>
        /// <param name="isGuest">A buyer with no account, who gets a sign-up link instead of an account link.</param>
        Task SendAsync(Guid tenantId, string toEmail, string toName, Guid redemptionToken,
            string kind, int amountCents, DateTime? validOnDate, bool isGuest);
    }

    public class PurchaseConfirmationEmailer : IPurchaseConfirmationEmailer
    {
        private readonly ISmtpEmailer _emailer;
        private readonly IEmailSuppressionRepository _suppression;
        private readonly ITenantRepository _tenants;
        private readonly IConfiguration _config;
        private readonly ILogger<PurchaseConfirmationEmailer> _logger;

        public PurchaseConfirmationEmailer(
            ISmtpEmailer emailer,
            IEmailSuppressionRepository suppression,
            ITenantRepository tenants,
            IConfiguration config,
            ILogger<PurchaseConfirmationEmailer> logger)
        {
            _emailer = emailer;
            _suppression = suppression;
            _tenants = tenants;
            _config = config;
            _logger = logger;
        }

        public async Task SendAsync(Guid tenantId, string toEmail, string toName, Guid redemptionToken,
            string kind, int amountCents, DateTime? validOnDate, bool isGuest)
        {
            if (!_emailer.IsConfigured) return;
            // Skip hard-bounced addresses (scope='all'); a dead address only inflates the
            // account-wide bounce rate. Marketing opt-outs don't block a transactional receipt.
            if (await _suppression.IsSuppressed(toEmail, tenantId, marketing: false)) return;
            try
            {
                var tenant = await _tenants.GetById(tenantId);
                if (tenant is null) return;
                var apex = _config["App:RootDomain"] ?? "ridepass.io";
                var baseUrl = $"https://{tenant.Subdomain}.{apex}";
                var qrUrl = $"{baseUrl}/api/Qr/{redemptionToken}";
                var profileUrl = $"{baseUrl}/User/MyPasses";

                var (subject, kindLabel) = kind switch
                {
                    "pass" => ($"Your {tenant.DisplayName} day pass", "pass"),
                    "event_ticket" => ($"Your {tenant.DisplayName} admission", "admission"),
                    "season_pass" => ($"Your {tenant.DisplayName} season pass", "season pass"),
                    _ => ($"Your {tenant.DisplayName} purchase", "purchase"),
                };

                var validLine = validOnDate.HasValue
                    ? $"<p>Valid on <strong>{validOnDate.Value:dddd, MMMM d, yyyy}</strong>.</p>"
                    : string.Empty;

                // Guests have no account, so point them at signup (email prefilled) instead of a
                // My-Passes link they can't use; logged-in riders get the account link as before.
                var accountLine = isGuest
                    ? $@"<p>Want to manage your entries, check in faster next time, and get race-day and waitlist alerts?
<a href=""{baseUrl}/SignUp?email={Uri.EscapeDataString(toEmail)}"">Create your free account</a>.</p>"
                    : $@"<p>You can also find this {kindLabel} on your account at <a href=""{profileUrl}"">{profileUrl}</a>.</p>";

                var html = $@"<p>Hi {System.Net.WebUtility.HtmlEncode(toName)},</p>
<p>Thanks for your {kindLabel} from <strong>{System.Net.WebUtility.HtmlEncode(tenant.DisplayName)}</strong>.
Total: <strong>${(amountCents / 100m):0.00}</strong>.</p>
{validLine}
<p>Show this QR at the gate to be checked in:</p>
<p><img src=""{qrUrl}"" alt=""Your QR code"" width=""240"" height=""240"" style=""border:1px solid #ddd; padding:6px; background:#fff"" /></p>
<p>If your email client doesn't show the image, open <a href=""{qrUrl}"">this link</a> on your phone — it'll display the QR.</p>
{accountLine}";

                await _emailer.Send(toEmail, subject, html, null, Services.Email.TenantEmailIdentity.For(tenant));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send {Kind} confirmation email to {Email}", kind, toEmail);
            }
        }
    }
}
