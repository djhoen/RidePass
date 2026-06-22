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

        /// <summary>Add amount back to the card's balance; un-deplete it if it was depleted. Used to
        /// reverse a hold when the checkout that applied the card failed or was abandoned.</summary>
        Task RestoreBalance(Guid id, int amountCents);

        /// <summary>Delete the redemption rows for the given sources and RETURN the deleted rows
        /// (so the caller can restore exactly what it removed). Race-safe: only the call that wins
        /// the delete gets the rows back, so a concurrent retry restores nothing.</summary>
        Task<List<GiftCardRedemption>> DeleteRedemptionsBySource(string sourceKind, IReadOnlyList<Guid> sourceIds);

        /// <summary>Flip a freshly-minted 'pending' card to 'active' once its purchase is paid.
        /// Guarded on status='pending' so a duplicate webhook is a no-op.</summary>
        Task Activate(Guid id);

        /// <summary>Void a 'pending' card whose purchase failed/was abandoned, so it can never be
        /// spent or delivered. Guarded on status='pending'.</summary>
        Task Void(Guid id);

        /// <summary>Mark a pending card delivered (called after a successful email send).</summary>
        Task MarkDelivered(Guid id);

        /// <summary>Used by the scheduled-delivery worker to find cards due for sending.</summary>
        Task<List<GiftCard>> ListPendingDelivery(DateTime cutoffUtc, int take);

        /// <summary>Used to determine if the card can be refunded ("balance untouched").</summary>
        Task<int> CountRedemptions(Guid giftCardId);

        /// <summary>Total gift-card cents applied to one purchase, used by the card-first refund
        /// split so a refund returns the gift-card share to the card and only the rest to Stripe.</summary>
        Task<int> SumRedemptionsForSource(string sourceKind, Guid sourceId, Guid tenantId);

        Task<Guid> RecordRedemption(GiftCardRedemption r);
        Task<List<GiftCardRedemption>> ListRedemptionsByCard(Guid giftCardId);
    }
}
