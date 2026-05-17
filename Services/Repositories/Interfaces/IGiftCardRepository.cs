using Services.Repositories.Data.GiftCardData;

namespace Services.Repositories.Interfaces
{
    public interface IGiftCardRepository
    {
        Task<Guid> Create(GiftCard card);
        Task<GiftCard?> GetById(Guid id, Guid tenantId);
        Task<GiftCard?> GetByCode(Guid tenantId, string code);
        Task<GiftCard?> GetByPaymentIntentId(string paymentIntentId);

        /// <summary>Stamp the Stripe PI on the card after PaymentIntents.Create returns.</summary>
        Task SetStripePaymentIntentId(Guid id, string paymentIntentId);

        /// <summary>Lower the card's balance by amount; flip status to 'depleted' when it hits zero.</summary>
        Task ApplyToBalance(Guid id, int amountCents);

        /// <summary>Mark a pending card delivered (called after a successful email send).</summary>
        Task MarkDelivered(Guid id);

        /// <summary>Used by the scheduled-delivery worker to find cards due for sending.</summary>
        Task<List<GiftCard>> ListPendingDelivery(DateTime cutoffUtc, int take);

        /// <summary>Used to determine if the card can be refunded ("balance untouched").</summary>
        Task<int> CountRedemptions(Guid giftCardId);

        Task<Guid> RecordRedemption(GiftCardRedemption r);
        Task<List<GiftCardRedemption>> ListRedemptionsByCard(Guid giftCardId);
    }
}
