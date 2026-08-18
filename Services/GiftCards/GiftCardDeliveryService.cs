using Microsoft.Extensions.Logging;
using Services.Helpers;
using Services.Repositories.Data.GiftCardData;
using Services.Repositories.Interfaces;

namespace Services.GiftCards
{
    public interface IGiftCardDeliveryService
    {
        Task<bool> SendDeliveryEmail(GiftCard card);
    }

    /// <summary>
    /// Composes and sends the gift-card delivery email, then marks the card delivered.
    /// Used by both the Stripe webhook (immediate, when no schedule was requested) and
    /// the scheduled-delivery hosted worker (when ScheduledDeliveryAtUtc is in the past).
    /// </summary>
    public class GiftCardDeliveryService : IGiftCardDeliveryService
    {
        private readonly ISmtpEmailer _emailer;
        private readonly ITenantRepository _tenants;
        private readonly IGiftCardRepository _giftCards;
        private readonly IEmailSuppressionRepository _suppression;
        private readonly ILogger<GiftCardDeliveryService> _logger;

        public GiftCardDeliveryService(
            ISmtpEmailer emailer,
            ITenantRepository tenants,
            IGiftCardRepository giftCards,
            IEmailSuppressionRepository suppression,
            ILogger<GiftCardDeliveryService> logger)
        {
            _emailer = emailer;
            _tenants = tenants;
            _giftCards = giftCards;
            _suppression = suppression;
            _logger = logger;
        }

        public async Task<bool> SendDeliveryEmail(GiftCard card)
        {
            if (!_emailer.IsConfigured) return false;
            // Imported cards have no recipient to email (created delivery_status='delivered', so
            // this shouldn't be reached for them; belt-and-braces for the nullable columns).
            if (string.IsNullOrWhiteSpace(card.RecipientEmail)) return false;
            try
            {
                var tenant = await _tenants.GetById(card.TenantId);
                if (tenant is null) return false;

                // Never email a hard-bounced / globally-suppressed address: it would inflate the
                // account-wide SES bounce rate. Transactional (marketing:false), so a marketing-only
                // opt-out doesn't block a legitimate gift delivery. Mark delivered so the scheduled
                // worker doesn't retry a dead address forever.
                if (await _suppression.IsSuppressed(card.RecipientEmail, card.TenantId, marketing: false))
                {
                    _logger.LogWarning("Skipping gift-card delivery for card {Id}: recipient address is suppressed.", card.Id);
                    await _giftCards.MarkDelivered(card.Id);
                    return false;
                }

                var note = string.IsNullOrEmpty(card.PersonalNote)
                    ? string.Empty
                    : $"<blockquote style=\"border-left:3px solid #ccc;padding-left:1em;color:#555\">{System.Net.WebUtility.HtmlEncode(card.PersonalNote)}</blockquote>";

                var subject = $"You received a {tenant.DisplayName} gift card!";
                var html = $@"<p>Hi {System.Net.WebUtility.HtmlEncode(card.RecipientName)},</p>
<p><strong>{System.Net.WebUtility.HtmlEncode(card.BuyerName)}</strong> sent you a gift card to <strong>{System.Net.WebUtility.HtmlEncode(tenant.DisplayName)}</strong> worth <strong>${card.InitialAmountCents / 100m:0.00}</strong>.</p>
{note}
<p>Use this code at checkout — it works on any purchase (passes, race tickets, season passes) until the balance runs out:</p>
<p style=""font-family:monospace;font-size:1.4em;font-weight:bold;padding:8px 14px;border:1px solid #ddd;display:inline-block"">
  {System.Net.WebUtility.HtmlEncode(card.Code)}
</p>";

                await _emailer.Send(card.RecipientEmail, subject, html, null, Services.Email.TenantEmailIdentity.For(tenant));
                await _giftCards.MarkDelivered(card.Id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send gift-card email for card {Id}", card.Id);
                return false;
            }
        }
    }
}
