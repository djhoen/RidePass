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
        Task<List<RecentSalesItem>> List(Guid tenantId, DateTime? fromUtc, DateTime? toUtc, string? status, int limit);
    }
}
