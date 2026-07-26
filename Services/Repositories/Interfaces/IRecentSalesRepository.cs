using Services.Repositories.Data.PaymentData;

namespace Services.Repositories.Interfaces
{
    /// <summary>
    /// Reads from the v_recent_sales database view (see Script0080) - the only
    /// repo that should query that view. Every cross-cutting "any kind of sale"
    /// feature in the admin should call this instead of UNION-ing the seven
    /// per-kind purchase tables by hand.
    /// </summary>
    public interface IRecentSalesRepository
    {
        /// <summary>
        /// Tenant-scoped, paged read of v_recent_sales. `statuses` null/empty means the default
        /// admin view: everything except 'abandoned' (a checkout our own reconciler gave up on
        /// with no completed payment attempt, not a real decline). Pass 'abandoned' explicitly in
        /// `statuses` to include it. `kinds` null/empty means every kind. Returns the rows for the
        /// requested page plus the total row count matching the filters, ignoring offset/limit, so
        /// callers can page.
        /// </summary>
        Task<(List<RecentSalesItem> Rows, int Total)> List(Guid tenantId, DateTime? fromUtc, DateTime? toUtc,
            IReadOnlyCollection<string>? statuses, IReadOnlyCollection<string>? kinds, int offset, int limit,
            string? email = null, string? orderId = null, bool includeAbandoned = false);

        /// <summary>
        /// Every line in the same order as the anchor purchase: all sales sharing the anchor's
        /// Stripe PaymentIntent (race entry + gate fees + add-ons + bundled membership/season
        /// pass). Cash / fully-gift-card-covered orders have no shared intent, so only the anchor
        /// line is returned. Tenant-scoped.
        /// </summary>
        Task<List<RecentSalesItem>> ListOrder(Guid tenantId, string kind, Guid id);
    }
}
