using Services.Repositories.Data.ReportData;

namespace Services.Repositories.Interfaces
{
    public interface IReportsRepository
    {
        Task<SalesTotals> GetDayPassTotals(Guid tenantId, DateTime fromUtc, DateTime toUtc);
        Task<SalesTotals> GetTicketTotals(Guid tenantId, DateTime fromUtc, DateTime toUtc);
        Task<int> GetUniqueRiders(Guid tenantId, DateTime fromUtc, DateTime toUtc);
        Task<int> GetDisputeCount(Guid tenantId, DateTime fromUtc, DateTime toUtc);
        Task<List<DailyRevenuePoint>> GetDailyRevenue(Guid tenantId, DateTime fromUtc, DateTime toUtc, string timezone);
        Task<List<TopDayPassProductRow>> GetTopDayPassProducts(Guid tenantId, DateTime fromUtc, DateTime toUtc, int limit = 10);
        Task<List<TopEventRow>> GetTopEvents(Guid tenantId, DateTime fromUtc, DateTime toUtc, int limit = 10);

        Task<PlatformSalesTotals> GetPlatformTotals(DateTime fromUtc, DateTime toUtc);
        Task<List<DailyRevenuePoint>> GetPlatformDailyRevenue(DateTime fromUtc, DateTime toUtc);
        Task<List<TenantBreakdownRow>> GetTenantBreakdown(DateTime fromUtc, DateTime toUtc);
    }
}
