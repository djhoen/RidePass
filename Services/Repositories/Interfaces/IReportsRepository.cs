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

        /// <summary>
        /// How many season passes were actually bought in the period. status = 'paid' only: a
        /// pending row is an abandoned checkout, and a refunded / cancelled / upgraded row is not a
        /// pass anybody holds. This is the number behind the Sales Summary "Season passes" tile.
        /// </summary>
        Task<int> GetSeasonPassesSold(Guid tenantId, DateTime fromUtc, DateTime toUtc);

        /// <summary>Best-selling season pass products in the period, by revenue. Paid rows only.</summary>
        Task<List<TopPassProductRow>> GetTopSeasonPassProducts(Guid tenantId, DateTime fromUtc, DateTime toUtc, int limit = 5);

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
        /// <param name="purchaseTypes">Optional <see cref="RiderPurchaseTypes"/> values to keep; null/empty = all.</param>
        /// <param name="eventTypeCodes">Optional tenant_event_type codes to keep; null/empty = all.</param>
        Task<List<RiderReportRow>> GetRidersByRange(Guid tenantId, DateTime fromUtc, DateTime toUtc,
            string? search, int cap, string audience = "rider",
            IReadOnlyList<string>? purchaseTypes = null, IReadOnlyList<string>? eventTypeCodes = null);

        /// <summary>Everything one rider is registered for (last year + upcoming), matched by
        /// user id and/or email. Same row shape as the range report.</summary>
        Task<List<RiderReportRow>> GetRiderRegistrations(Guid tenantId, Guid? userId, string? email);

        /// <summary>Waivers this rider has signed, newest first, matched by user id and/or email.
        /// Carries a HasSignatureImage flag rather than the image itself.</summary>
        Task<List<RiderWaiverRow>> GetRiderWaivers(Guid tenantId, Guid? userId, string? email);

        /// <summary>One signature's image data URL, tenant-scoped. Null when the signature is
        /// unknown to this tenant or has no stored image.</summary>
        Task<string?> GetWaiverSignatureImage(Guid tenantId, Guid signatureId);

        /// <summary>Identity + lifetime totals for the rider drill-in header, matched by account
        /// id and/or email. Falls back to purchase-captured details for guests with no account.</summary>
        Task<RiderProfileRow?> GetRiderProfile(Guid tenantId, Guid? userId, string? email);
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
