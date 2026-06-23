using Services.Repositories.Data.PaymentData;

namespace Services.Repositories.Interfaces
{
    /// <summary>
    /// Reads from the v_recent_sales database view (see Script0080) — the only
    /// repo that should query that view. Every cross-cutting "any kind of sale"
    /// feature in the admin should call this instead of UNION-ing the seven
    /// per-kind purchase tables by hand.
    /// </summary>
    public interface IRecentSalesRepository
    {
        Task<List<RecentSalesItem>> List(Guid tenantId, DateTime? fromUtc, DateTime? toUtc, string? status, int limit,
            string? email = null, string? orderId = null);

        /// <summary>
        /// Every line in the same order as the anchor purchase: all sales sharing the anchor's
        /// Stripe PaymentIntent (race entry + gate fees + add-ons + bundled membership/season
        /// pass). Cash / fully-gift-card-covered orders have no shared intent, so only the anchor
        /// line is returned. Tenant-scoped.
        /// </summary>
        Task<List<RecentSalesItem>> ListOrder(Guid tenantId, string kind, Guid id);
    }
}
