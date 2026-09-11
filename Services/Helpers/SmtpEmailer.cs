using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Services.Delivery;

namespace Services.Helpers
{
    /// <summary>
    /// Who a message appears to come from. The envelope address is always the platform's
    /// authenticated FromAddress (we're DKIM-signed for ridepass.io and can't sign for a track's
    /// own domain), so the tenant's identity is carried by the display name and the Reply-To:
    /// the rider sees "Motoland" in their inbox, and hitting reply reaches the track, not a
    /// noreply mailbox. Null anywhere = fall back to the platform defaults.
    /// </summary>
    public record EmailSender(string? FromName, string? ReplyToEmail = null, string? ReplyToName = null,
        string? FromAddress = null,
        // The tenant the email is sent as. When present, a bare body (a receipt written as a few
        // paragraphs) is dressed in that tenant's header and footer at send time.
        Services.Repositories.Data.TenantData.Tenant? Tenant = null);

    public interface ISmtpEmailer
    {
        bool IsConfigured { get; }
        Task<bool> Send(string toEmail, string subject, string htmlBody);
        // Overload that stamps extra headers (e.g. List-Unsubscribe / List-Unsubscribe-Post
        // for marketing one-click unsubscribe).
        Task<bool> Send(string toEmail, string subject, string htmlBody, IReadOnlyDictionary<string, string>? headers);
        // Overload that sends AS a tenant: their name on the From line, replies routed to them.
        // Every rider-facing email should use this, so no rider gets mail from a platform they've
        // never heard of about a track they have.
        Task<bool> Send(string toEmail, string subject, string htmlBody,
            IReadOnlyDictionary<string, string>? headers, EmailSender? sender);
    }

    /// <summary>
    /// Config-gated SMTP sender. Reads Email:Smtp:Host / Port / User / Password and Email:FromAddress / FromName.
    /// If Host or FromAddress is missing, IsConfigured is false and Send is a silent no-op so notification
    /// emission code doesn't have to special-case "email not set up yet" deployments.
    /// </summary>
    public class SmtpEmailer : ISmtpEmailer
    {
        private readonly IConfiguration _config;
        private readonly ILogger<SmtpEmailer> _logger;
        private readonly IOutboundDeliveryGate? _gate;
        private readonly Services.Repositories.Interfaces.ITenantBrandingRepository? _brandings;
        // Logo and colors per tenant, refreshed every minute: a receipt burst should not hit the
        // branding table once per email, and a logo change need not wait for a restart.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, (DateTime At, Services.Repositories.Data.TenantData.TenantBranding? Row)> _brandingCache = new();
        private static readonly TimeSpan BrandingTtl = TimeSpan.FromMinutes(1);

        public bool IsConfigured { get; }

        /// <summary>
        /// Every email sent AS a tenant wears that tenant's header (logo or name) and footer (name,
        /// address, phone, socials). Callers write the middle as plain paragraphs; marketing mail
        /// arrives already composed and is passed through, as is any full HTML document.
        /// </summary>
        private async Task<string> Dress(string htmlBody, EmailSender? sender)
        {
            var tenant = sender?.Tenant;
            if (tenant is null || Services.Email.EmailHtml.IsComposed(htmlBody)) return htmlBody;
            try
            {
                var rootDomain = _config["Tenant:RootDomain"] ?? _config["App:RootDomain"] ?? "ridepass.io";
                var siteUrl = $"https://{tenant.Subdomain}.{rootDomain}";
                var branding = await BrandingFor(tenant.Id);
                // The marketing footer is for marketing; a receipt gets the address block only.
                var brand = Services.Email.EmailBranding.From(tenant, branding, siteUrl) with { CustomFooterHtml = null };
                return Services.Email.EmailHtml.Compose(htmlBody, null, brand, null);
            }
            catch (Exception ex)
            {
                // Dressing is cosmetic; a branding lookup failure must not lose a receipt.
                _logger.LogWarning(ex, "Could not apply tenant branding to an email for {Tenant}; sending plain", tenant.Subdomain);
                return htmlBody;
            }
        }

        private async Task<Services.Repositories.Data.TenantData.TenantBranding?> BrandingFor(Guid tenantId)
        {
            if (_brandings is null) return null;
            if (_brandingCache.TryGetValue(tenantId, out var hit) && DateTime.UtcNow - hit.At < BrandingTtl) return hit.Row;
            var row = await _brandings.GetByTenantId(tenantId);
            _brandingCache[tenantId] = (DateTime.UtcNow, row);
            return row;
        }

        // The gate is optional so existing hand-wired constructions keep compiling, but every
        // real deployment (web API DI + TaskRunner) passes one: it is the super-admin
        // kill switch / allowlist and must sit at this last hop so no caller can bypass it.
        // The branding repository is optional the same way; without it a tenant email still gets
        // the tenant's name, address, and socials, only without the logo and color.
        public SmtpEmailer(IConfiguration config, ILogger<SmtpEmailer> logger, IOutboundDeliveryGate? gate = null,
            Services.Repositories.Interfaces.ITenantBrandingRepository? brandings = null)
        {
            _config = config;
            _logger = logger;
            _gate = gate;
            _brandings = brandings;
            IsConfigured = !string.IsNullOrWhiteSpace(config["Email:Smtp:Host"])
                        && !string.IsNullOrWhiteSpace(config["Email:FromAddress"]);
        }

        public Task<bool> Send(string toEmail, string subject, string htmlBody)
            => Send(toEmail, subject, htmlBody, null, null);

        public Task<bool> Send(string toEmail, string subject, string htmlBody,
            IReadOnlyDictionary<string, string>? headers)
            => Send(toEmail, subject, htmlBody, headers, null);

        public async Task<bool> Send(string toEmail, string subject, string htmlBody,
            IReadOnlyDictionary<string, string>? headers, EmailSender? sender)
        {
            if (!IsConfigured) return false;
            if (_gate is not null)
            {
                var block = await _gate.BlockReason(DeliveryChannel.Email, toEmail);
                if (block is not null)
                {
                    _logger.LogInformation("Suppressed email to {Email} ({Subject}): {Reason}", toEmail, subject, block);
                    return false;
                }
            }
            htmlBody = await Dress(htmlBody, sender);
            try
            {
                var host = _config["Email:Smtp:Host"]!;
                var port = int.TryParse(_config["Email:Smtp:Port"], out var p) ? p : 587;
                var user = _config["Email:Smtp:User"];
                var pass = _config["Email:Smtp:Password"];
                // A tenant may send from its own address ONLY under the domain SendGrid signs for
                // (noreply@highland.ridepass.io). Anything else would go out unsigned for its domain
                // and fail DMARC, so the platform address wins and the mismatch is logged.
                var platformFrom = _config["Email:FromAddress"]!;
                var fromAddr = platformFrom;
                if (!string.IsNullOrWhiteSpace(sender?.FromAddress))
                {
                    if (Services.Email.EmailSendingPolicy.IsUnderSendingDomain(sender!.FromAddress!,
                            Services.Email.EmailSendingPolicy.SendingDomain(_config)))
                        fromAddr = sender.FromAddress!;
                    else
                        _logger.LogWarning("Ignoring tenant from-address '{From}': not under the sending domain; using {Platform}",
                            sender.FromAddress, fromAddr);
                }
                // The tenant's name when the caller supplied one, else the platform default.
                var fromName = string.IsNullOrWhiteSpace(sender?.FromName)
                    ? (_config["Email:FromName"] ?? "RidePass")
                    : sender!.FromName!;

                using var client = new SmtpClient(host, port)
                {
                    EnableSsl = true,
                    Credentials = string.IsNullOrEmpty(user) ? null : new NetworkCredential(user, pass),
                };
                using var msg = new MailMessage
                {
                    From = new MailAddress(fromAddr, fromName),
                    Subject = subject,
                    Body = htmlBody,
                    IsBodyHtml = true,
                };
                msg.To.Add(toEmail);
                // Replies go to the track that actually sold the ticket. A malformed contact email
                // on the tenant record must not take the whole send down, so it's best-effort.
                if (!string.IsNullOrWhiteSpace(sender?.ReplyToEmail))
                {
                    try { msg.ReplyToList.Add(new MailAddress(sender!.ReplyToEmail!, sender.ReplyToName ?? fromName)); }
                    catch (FormatException)
                    {
                        _logger.LogWarning("Tenant reply-to '{ReplyTo}' is not a valid address; sending without it.",
                            sender!.ReplyToEmail);
                    }
                }
                if (headers is not null)
                {
                    foreach (var h in headers) msg.Headers.Add(h.Key, h.Value);
                }
                try
                {
                    await client.SendMailAsync(msg);
                }
                catch (SmtpException ex) when (fromAddr != platformFrom
                    && ex.Message.Contains("Sender Identity", StringComparison.OrdinalIgnoreCase))
                {
                    // The relay does not (yet) recognise the tenant's address as a verified sender,
                    // e.g. the track's subdomain is not authenticated in SendGrid. Losing the email
                    // is worse than losing the branding: resend once from the platform address on a
                    // fresh connection (the first session is dead after the rejected DATA).
                    _logger.LogWarning(
                        "Relay rejected tenant from-address '{From}' as an unverified sender; resending from {Platform}",
                        fromAddr, platformFrom);
                    msg.From = new MailAddress(platformFrom, fromName);
                    using var retryClient = new SmtpClient(host, port) { EnableSsl = true, Credentials = client.Credentials };
                    await retryClient.SendMailAsync(msg);
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send email to {Email}", toEmail);
                return false;
            }
        }
    }
}
