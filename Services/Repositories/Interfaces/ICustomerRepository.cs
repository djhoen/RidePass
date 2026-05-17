using Services.Repositories.Data.CustomerData;

namespace Services.Repositories.Interfaces
{
    public interface ICustomerRepository
    {
        // Distinct users with any activity (paid purchase or signed waiver) at this
        // tenant. Search matches first name, last name, or email (case-insensitive).
        Task<List<CustomerSummary>> ListForTenant(Guid tenantId, string? search, int limit, int offset);

        // Total count for the same filter — used by the list page's pagination footer.
        Task<int> CountForTenant(Guid tenantId, string? search);

        // Single user + all their activity at this tenant. Returns null if the user
        // has zero activity at this tenant (which means the tenant shouldn't be
        // looking at them in the first place — protects against URL fishing).
        Task<CustomerDetail?> GetDetail(Guid userId, Guid tenantId);

        // Top N users by either count of paid passes (metric="days") or total paid
        // (metric="spent"). period is "month" or "year" — both anchored to the
        // current calendar month/year. Both metrics are populated on every row so
        // the UI can flip tabs without re-querying.
        Task<List<TopRiderEntry>> GetTopRiders(Guid tenantId, string metric, string period, int limit);
    }
}
