using Services.Repositories.Data.ReportData;

namespace Services.Repositories.Interfaces
{
    public interface IReportsRepository
    {
        Task<SalesTotals> GetTicketTotals(Guid tenantId, DateTime fromUtc, DateTime toUtc);
        Task<int> GetUniqueRiders(Guid tenantId, DateTime fromUtc, DateTime toUtc);
        Task<int> GetDisputeCount(Guid tenantId, DateTime fromUtc, DateTime toUtc);
        Task<List<DailyRevenuePoint>> GetDailyRevenue(Guid tenantId, DateTime fromUtc, DateTime toUtc, string timezone);
        Task<List<TopEventRow>> GetTopEvents(Guid tenantId, DateTime fromUtc, DateTime toUtc, int limit = 10);

        Task<PlatformSalesTotals> GetPlatformTotals(DateTime fromUtc, DateTime toUtc);
        Task<List<DailyRevenuePoint>> GetPlatformDailyRevenue(DateTime fromUtc, DateTime toUtc);
        Task<List<TenantBreakdownRow>> GetTenantBreakdown(DateTime fromUtc, DateTime toUtc);

        /// <summary>All registrants for an event — pass purchasers, ticket purchasers, and season-pass holders who reserved.</summary>
        Task<List<EventRiderRow>> GetEventRiders(Guid tenantId, Guid eventId);

        /// <summary>One row per scheduled event in [fromUtc, toUtc) with registered/checked-in/revenue aggregates.</summary>
        Task<List<DailyEventRow>> GetEventsInRange(Guid tenantId, DateTime fromUtc, DateTime toUtc);

        /// <summary>
        /// Resolve a redemption token (pass / event ticket / season pass) to the rider
        /// and gather all of their today + future registrations across all three sources.
        /// Returns null when no row matches the token in this tenant.
        /// </summary>
        Task<CheckInLookup?> LookupCheckInByToken(Guid tenantId, Guid token, DateTime fromUtc, DateTime toUtc);
    }
}
