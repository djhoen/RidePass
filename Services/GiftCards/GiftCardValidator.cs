using Services.Repositories.Data.GiftCardData;
using Services.Repositories.Interfaces;

namespace Services.GiftCards
{
    public interface IGiftCardValidator
    {
        /// <summary>
        /// Looks up a gift card by code (case-insensitive) and returns the chunk that
        /// can be applied to a given post-discount line total. Caps at remaining
        /// balance and at the line total. Caller is responsible for applying the
        /// returned amount and recording a gift_card_redemption row.
        /// </summary>
        Task<(GiftCardApplication? application, string? error)> ResolveAsync(
            Guid tenantId, string code, int postDiscountSubtotalCents);
    }

    public class GiftCardValidator : IGiftCardValidator
    {
        private readonly IGiftCardRepository _cards;

        public GiftCardValidator(IGiftCardRepository cards) => _cards = cards;

        public async Task<(GiftCardApplication? application, string? error)> ResolveAsync(
            Guid tenantId, string code, int postDiscountSubtotalCents)
        {
            if (string.IsNullOrWhiteSpace(code)) return (null, "Gift card code is empty.");
            var card = await _cards.GetByCode(tenantId, code.Trim());
            if (card is null) return (null, "That gift card code isn't valid here.");
            if (card.Status == "refunded") return (null, "That gift card has been refunded.");
            if (card.Status == "depleted" || card.BalanceCents <= 0)
                return (null, "That gift card has no balance remaining.");
            // 'pending' (purchase not yet paid) and 'void' (purchase failed/abandoned) are never
            // spendable. Only a paid, active card can be applied.
            if (card.Status != "active")
                return (null, "That gift card isn't active.");

            // Don't let a delivery-pending or future-scheduled card be redeemed before the
            // recipient has received it. Buyer can't apply a card they bought for someone else.
            if (card.DeliveryStatus == "pending" && card.ScheduledDeliveryAtUtc.HasValue
                && card.ScheduledDeliveryAtUtc.Value > DateTime.UtcNow)
            {
                return (null, "This gift card hasn't been delivered yet.");
            }

            // Apply min(balance, what's owed). The caller decides what to do with leftover
            // (usually: charge the rest to Stripe).
            var apply = Math.Min(card.BalanceCents, postDiscountSubtotalCents);
            if (apply <= 0) return (null, "This purchase is already $0.");

            return (new GiftCardApplication { Card = card, AmountToApplyCents = apply }, null);
        }
    }
}
