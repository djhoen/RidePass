using Services.Helpers.Interfaces;
using Services.Repositories.Data.ReportData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class EndOfDayReportRepository : IEndOfDayReportRepository
    {
        private readonly IDbHelper _db;

        public EndOfDayReportRepository(IDbHelper db) => _db = db;

        // v_accounting_entries already buckets into the tenant's OWN calendar day (business_date is
        // occurred_at_utc AT TIME ZONE the tenant's timezone), so the date predicate needs no
        // timezone argument and no UTC range arithmetic. This is the same predicate
        // AccountingEntryRepository.ListForBusinessDate uses to build the journal entry.
        public async Task<List<AccountingBucketRow>> GetDayBuckets(Guid tenantId, DateOnly businessDate)
        {
            const string sql = @"
                SELECT entry_kind                                    AS EntryKind,
                       source_kind                                   AS SourceKind,
                       -- In the GROUP BY so a lesson's tickets split off from the gate's here the
                       -- same way they do in the journal entry. Without it the report would roll
                       -- both into one 'event_ticket' bucket and silently disagree with what the
                       -- sync actually posted.
                       revenue_key_override                          AS RevenueKeyOverride,
                       payment_method                                AS PaymentMethod,
                       COUNT(*)::int                                 AS EntryCount,
                       COALESCE(SUM(gross_cents), 0)::bigint         AS GrossCents,
                       COALESCE(SUM(stripe_fee_cents), 0)::bigint    AS StripeFeeCents,
                       COALESCE(SUM(ridepass_cut_cents), 0)::bigint  AS RidepassCutCents,
                       COALESCE(SUM(net_to_tenant_cents), 0)::bigint AS NetToTenantCents,
                       COALESCE(SUM(tax_cents), 0)::bigint           AS TaxCents,
                       COALESCE(SUM(tip_cents), 0)::bigint           AS TipCents,
                       COALESCE(SUM(gift_card_applied_cents), 0)::bigint AS GiftCardAppliedCents,
                       -- Counted here rather than inferred in C#: gift_card_applied_cents is
                       -- summed across the bucket, so a bucket carrying gift-card cents says
                       -- nothing about how many of its rows actually used one.
                       COUNT(*) FILTER (WHERE gift_card_applied_cents <> 0)::int AS GiftCardEntryCount
                FROM v_accounting_entries
                WHERE tenant_id = @tenantId
                  AND business_date = @businessDate
                GROUP BY entry_kind, source_kind, revenue_key_override, payment_method";
            return (await _db.Query<AccountingBucketRow>(sql, new { tenantId, businessDate })).ToList();
        }

        // Seller attribution lives on tenant_ledger_entry, not on the view, so the view is joined
        // back to its own ledger row. Synthesized rows (gift-card sales, rental-deposit lifecycle)
        // have a null ledger_entry_id and drop out of the join, which is correct: nobody rang them up.
        //
        // The users join is not tenant-filtered on purpose. u.id comes from a ledger row already
        // constrained to @tenantId, so it is a lookup along a scoped FK, not a tenant-crossing read,
        // and filtering on u.tenant_id would blank the name of a platform-level operator who made a
        // sale on the tenant's behalf. It is the same shape as ConcessionRepository.GetEmployeeSales.
        public async Task<List<EndOfDayStaffRow>> GetDayStaff(Guid tenantId, DateOnly businessDate)
        {
            const string sql = @"
                SELECT l.sold_by_user_id AS UserId,
                       NULLIF(TRIM(COALESCE(u.first_name, '') || ' ' || COALESCE(u.last_name, '')), '') AS Name,
                       u.email AS Email,
                       COUNT(*) FILTER (WHERE v.entry_kind = 'sale')::int   AS SaleCount,
                       COUNT(*) FILTER (WHERE v.entry_kind = 'refund')::int AS RefundCount,
                       COALESCE(SUM(v.gross_cents), 0)::bigint AS GrossCents,
                       -- Cents that actually reached the drawer, so this reconciles against a
                       -- turn-in: a counter sale part-funded by a gift card never put that part in
                       -- the till. Same subtraction the cash tender line makes.
                       COALESCE(SUM(v.gross_cents - v.gift_card_applied_cents)
                                FILTER (WHERE v.payment_method = 'cash'), 0)::bigint AS CashCents
                FROM v_accounting_entries v
                JOIN tenant_ledger_entry l
                      ON l.id = v.ledger_entry_id
                     AND l.tenant_id = v.tenant_id
                LEFT JOIN users u ON u.id = l.sold_by_user_id
                WHERE v.tenant_id = @tenantId
                  AND v.business_date = @businessDate
                  AND v.entry_kind IN ('sale', 'refund')
                  AND l.sold_by_user_id IS NOT NULL
                GROUP BY l.sold_by_user_id, u.first_name, u.last_name, u.email
                ORDER BY GrossCents DESC";
            return (await _db.Query<EndOfDayStaffRow>(sql, new { tenantId, businessDate })).ToList();
        }

        // cash_session has no business_date column, so the day is derived the same way the view
        // derives its own: the tenant's local calendar date of opened_at. A session opened at
        // 11pm and closed after midnight belongs to the day it was OPENED, which is how a shift reads.
        public async Task<List<EndOfDayCashSessionRow>> GetDayCashSessions(Guid tenantId, DateOnly businessDate, string timezone)
        {
            const string sql = @"
                SELECT s.id                    AS Id,
                       s.user_id               AS UserId,
                       NULLIF(TRIM(COALESCE(u.first_name, '') || ' ' || COALESCE(u.last_name, '')), '') AS UserName,
                       e.title                 AS EventTitle,
                       s.device_id             AS DeviceId,
                       s.opening_float_cents::bigint AS OpeningFloatCents,
                       s.status                AS Status,
                       s.opened_at             AS OpenedAt,
                       s.closed_at             AS ClosedAt
                FROM cash_session s
                LEFT JOIN users u ON u.id = s.user_id
                LEFT JOIN event e ON e.id = s.event_id AND e.tenant_id = s.tenant_id
                WHERE s.tenant_id = @tenantId
                  AND (s.opened_at AT TIME ZONE @timezone)::date = @businessDate
                ORDER BY s.opened_at";
            return (await _db.Query<EndOfDayCashSessionRow>(sql, new { tenantId, businessDate, timezone })).ToList();
        }

        public async Task<List<EndOfDayTurnInRow>> GetDayCashTurnIns(Guid tenantId, DateOnly businessDate, string timezone)
        {
            const string sql = @"
                SELECT t.id                         AS Id,
                       NULLIF(TRIM(COALESCE(w.first_name, '') || ' ' || COALESCE(w.last_name, '')), '') AS WorkerName,
                       NULLIF(TRIM(COALESCE(m.first_name, '') || ' ' || COALESCE(m.last_name, '')), '') AS ManagerName,
                       t.expected_cents::bigint         AS ExpectedCents,
                       t.worker_counted_cents::bigint   AS WorkerCountedCents,
                       t.manager_counted_cents::bigint  AS ManagerCountedCents,
                       t.variance_cents::bigint         AS VarianceCents,
                       t.status                     AS Status,
                       t.note                       AS Note,
                       t.submitted_at               AS SubmittedAt,
                       t.confirmed_at               AS ConfirmedAt
                FROM cash_turn_in t
                LEFT JOIN users w ON w.id = t.worker_user_id
                LEFT JOIN users m ON m.id = t.manager_user_id
                WHERE t.tenant_id = @tenantId
                  AND (t.submitted_at AT TIME ZONE @timezone)::date = @businessDate
                ORDER BY t.submitted_at";
            return (await _db.Query<EndOfDayTurnInRow>(sql, new { tenantId, businessDate, timezone })).ToList();
        }

        // Ranged on occurred_at_utc rather than business_date so the caller's UTC window (the same
        // one every other Reports endpoint takes) is honored exactly; business_date is still carried
        // through so the per-day rollup stays on the tenant's clock.
        public async Task<List<SalesTaxBucketRow>> GetSalesTaxBuckets(Guid tenantId, DateTime fromUtc, DateTime toUtc)
        {
            const string sql = @"
                SELECT business_date                          AS BusinessDate,
                       source_kind                            AS SourceKind,
                       -- Same reason as GetDayBuckets: the tax report categorises by the SAME
                       -- QuickBooks slot the journal entry uses, so the override has to survive
                       -- the aggregate or a training department's tax would show under the gate.
                       revenue_key_override                   AS RevenueKeyOverride,
                       entry_kind                             AS EntryKind,
                       COALESCE(SUM(tax_cents), 0)::bigint    AS TaxCents,
                       COALESCE(SUM(gross_cents), 0)::bigint  AS GrossCents,
                       COUNT(*)::int                          AS EntryCount,
                       -- Only the rows that were actually taxed, for the same reason
                       -- GiftCardEntryCount exists above: a bucket mixing taxed and untaxed rows
                       -- would otherwise report all of them as taxed sales.
                       COUNT(*) FILTER (WHERE tax_cents <> 0)::int AS TaxedEntryCount,
                       COALESCE(SUM(gross_cents) FILTER (WHERE tax_cents <> 0), 0)::bigint AS TaxedGrossCents
                FROM v_accounting_entries
                WHERE tenant_id = @tenantId
                  AND entry_kind IN ('sale', 'refund')
                  AND occurred_at_utc >= @fromUtc AND occurred_at_utc < @toUtc
                GROUP BY business_date, source_kind, revenue_key_override, entry_kind
                ORDER BY business_date";
            return (await _db.Query<SalesTaxBucketRow>(sql, new { tenantId, fromUtc, toUtc })).ToList();
        }

        // Same shape and same range semantics as GetSalesTaxBuckets, minus the per-day axis: the
        // department report rolls a whole period up, so grouping by business_date would multiply
        // the row count by the length of the range for nothing.
        //
        // entry_kind is restricted to sale and refund for the same reason BuildEndOfDay restricts
        // it: everything else (gift-card sales, deposit lifecycle, chargebacks, RidePass's own
        // charges) is money that moved without being earned, and none of it belongs on a report
        // that asks which side of the business made money.
        public async Task<List<RevenueBucketRow>> GetRevenueBuckets(Guid tenantId, DateTime fromUtc, DateTime toUtc)
        {
            const string sql = @"
                SELECT source_kind                            AS SourceKind,
                       revenue_key_override                   AS RevenueKeyOverride,
                       entry_kind                             AS EntryKind,
                       COALESCE(SUM(gross_cents), 0)::bigint  AS GrossCents,
                       COALESCE(SUM(tax_cents), 0)::bigint    AS TaxCents,
                       COALESCE(SUM(tip_cents), 0)::bigint    AS TipCents,
                       COUNT(*)::int                          AS EntryCount
                FROM v_accounting_entries
                WHERE tenant_id = @tenantId
                  AND entry_kind IN ('sale', 'refund')
                  AND occurred_at_utc >= @fromUtc AND occurred_at_utc < @toUtc
                GROUP BY source_kind, revenue_key_override, entry_kind";
            return (await _db.Query<RevenueBucketRow>(sql, new { tenantId, fromUtc, toUtc })).ToList();
        }
    }
}
