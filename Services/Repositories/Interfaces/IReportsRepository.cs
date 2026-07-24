using Services.Repositories.Data.ReportData;

namespace Services.Repositories.Interfaces
{
    public interface IReportsRepository
    {
        Task<SalesTotals> GetTicketTotals(Guid tenantId, DateTime fromUtc, DateTime toUtc);

        /// <summary>Admission/amusement tax collected on event tickets in the period, for remittance.</summary>
        Task<AdmissionTaxTotals> GetAdmissionTaxTotals(Guid tenantId, DateTime fromUtc, DateTime toUtc);

        /// <summary>Gross sales per revenue type for the period, from the unified ledger
        /// (entry_kind='sale'). Sum across the rows for the all-kinds total.</summary>
        Task<List<RevenueByKindRow>> GetRevenueByKind(Guid tenantId, DateTime fromUtc, DateTime toUtc);

        Task<int> GetUniqueRiders(Guid tenantId, DateTime fromUtc, DateTime toUtc);
        Task<int> GetDisputeCount(Guid tenantId, DateTime fromUtc, DateTime toUtc);
        Task<List<DailyRevenuePoint>> GetDailyRevenue(Guid tenantId, DateTime fromUtc, DateTime toUtc, string timezone);
        Task<List<TopEventRow>> GetTopEvents(Guid tenantId, DateTime fromUtc, DateTime toUtc, int limit = 10);

        Task<PlatformSalesTotals> GetPlatformTotals(DateTime fromUtc, DateTime toUtc);
        Task<List<DailyRevenuePoint>> GetPlatformDailyRevenue(DateTime fromUtc, DateTime toUtc);
        Task<List<TenantBreakdownRow>> GetTenantBreakdown(DateTime fromUtc, DateTime toUtc);

        /// <summary>All registrants for an event — pass purchasers, ticket purchasers, and season-pass holders who reserved.</summary>
        Task<List<EventRiderRow>> GetEventRiders(Guid tenantId, Guid eventId);

        /// <summary>Date-range Rider Report: registrants (tickets + season-pass reservations)
        /// across every event starting in [fromUtc, toUtc), with linked wristband and waiver
        /// coverage. Search matches name, email, or wristband code (case-insensitive).
        /// Returns at most <paramref name="cap"/> rows; callers pass cap+1 to detect overflow.</summary>
        /// <param name="audience">"rider" (default) or "spectator"; spectators are ticket-only.</param>
        Task<List<RiderReportRow>> GetRidersByRange(Guid tenantId, DateTime fromUtc, DateTime toUtc,
            string? search, int cap, string audience = "rider");

        /// <summary>Everything one rider is registered for (last year + upcoming), matched by
        /// user id and/or email. Same row shape as the range report.</summary>
        Task<List<RiderReportRow>> GetRiderRegistrations(Guid tenantId, Guid? userId, string? email);

        /// <summary>Waivers this rider has signed, newest first, matched by user id and/or email.</summary>
        Task<List<RiderWaiverRow>> GetRiderWaivers(Guid tenantId, Guid? userId, string? email);
        Task<List<EventWaiverSignatureRow>> GetEventWaiverSignatures(Guid tenantId, Guid eventId);

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
