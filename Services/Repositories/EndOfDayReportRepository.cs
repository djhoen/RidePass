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
                                FILTER (WHERE v.payment_method = 'cash'), 0)::bigint AS CashCents,
                       -- The seller's share of the day's tips, from the same prorated column
                       -- Totals.TipsCents sums, so the by-worker tips table adds up to it.
                       COALESCE(SUM(v.tip_cents), 0)::bigint AS TipCents
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
                       t.worker_user_id             AS WorkerUserId,
                       NULLIF(TRIM(COALESCE(w.first_name, '') || ' ' || COALESCE(w.last_name, '')), '') AS WorkerName,
                       NULLIF(TRIM(COALESCE(m.first_name, '') || ' ' || COALESCE(m.last_name, '')), '') AS ManagerName,
                       t.expected_cents::bigint         AS ExpectedCents,
                       t.worker_counted_cents::bigint   AS WorkerCountedCents,
                       t.manager_counted_cents::bigint  AS ManagerCountedCents,
                       t.variance_cents::bigint         AS VarianceCents,
                       t.status                     AS Status,
                       t.note                       AS Note,
                       t.submitted_at               AS SubmittedAt,
                       t.confirmed_at               AS ConfirmedAt,
                       t.tips_earned_cents::bigint      AS TipsEarnedCents,
                       t.tips_from_drawer_cents::bigint AS TipsFromDrawerCents,
                       t.house_owes_cents::bigint       AS HouseOwesCents
                FROM cash_turn_in t
                LEFT JOIN users w ON w.id = t.worker_user_id
                LEFT JOIN users m ON m.id = t.manager_user_id
                WHERE t.tenant_id = @tenantId
                  AND (t.submitted_at AT TIME ZONE @timezone)::date = @businessDate
                ORDER BY t.submitted_at";
            return (await _db.Query<EndOfDayTurnInRow>(sql, new { tenantId, businessDate, timezone })).ToList();
        }

        // Tips handed to staff on the business date, per recipient and source. tip_payout has no
        // business_date column, so the day is the tenant's local date of created_at, the same
        // derivation the two cash queries above use. The users join follows an id already scoped
        // by p.tenant_id, as in GetDayStaff.
        public async Task<List<EndOfDayTipPayoutRow>> GetDayTipPayouts(Guid tenantId, DateOnly businessDate, string timezone)
        {
            const string sql = @"
                SELECT p.user_id                            AS UserId,
                       NULLIF(TRIM(COALESCE(u.first_name, '') || ' ' || COALESCE(u.last_name, '')), '') AS Name,
                       u.email                              AS Email,
                       p.source                             AS Source,
                       COALESCE(SUM(p.amount_cents), 0)::bigint AS AmountCents,
                       COUNT(*)::int                        AS PayoutCount
                FROM tip_payout p
                LEFT JOIN users u ON u.id = p.user_id
                WHERE p.tenant_id = @tenantId
                  AND (p.created_at AT TIME ZONE @timezone)::date = @businessDate
                GROUP BY p.user_id, u.first_name, u.last_name, u.email, p.source";
            return (await _db.Query<EndOfDayTipPayoutRow>(sql, new { tenantId, businessDate, timezone })).ToList();
        }

        // Tips each seller earned on the business date, read from the sales themselves rather than
        // the ledger: a refund's ledger row is attributed to whoever processed the refund, so the
        // view's per-seller tip figure would credit the refund to the wrong person. Counting only
        // PAID sales by sold_by_user_id gets it right by construction (a refunded sale has left
        // 'paid', so its tip simply no longer counts for the seller who rang it). The day is the
        // tenant's local date the sale was paid (a tab is tipped when it closes), the same clock the
        // claim pool and the cash queries above use. The users join follows an id already scoped
        // by cs.tenant_id, as in GetDayStaff.
        public async Task<List<EndOfDayTipsBySellerRow>> GetDayTipsBySeller(Guid tenantId, DateOnly businessDate, string timezone)
        {
            const string sql = @"
                SELECT cs.sold_by_user_id                    AS UserId,
                       NULLIF(TRIM(COALESCE(u.first_name, '') || ' ' || COALESCE(u.last_name, '')), '') AS Name,
                       u.email                              AS Email,
                       COALESCE(SUM(cs.tip_cents), 0)::bigint AS TipCents,
                       COUNT(*)::int                        AS SaleCount
                FROM concession_sale cs
                LEFT JOIN users u ON u.id = cs.sold_by_user_id
                WHERE cs.tenant_id = @tenantId
                  AND cs.status = 'paid'
                  AND cs.tip_cents > 0
                  AND cs.sold_by_user_id IS NOT NULL
                  AND (COALESCE(cs.paid_at, cs.created_at) AT TIME ZONE @timezone)::date = @businessDate
                GROUP BY cs.sold_by_user_id, u.first_name, u.last_name, u.email";
            return (await _db.Query<EndOfDayTipsBySellerRow>(sql, new { tenantId, businessDate, timezone })).ToList();
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

        // Per-day gross sale revenue by revenue slot, for the Sales Summary chart's profit-center
        // series. Ranged on occurred_at_utc (the caller's UTC window) but GROUPED on business_date,
        // which is the tenant's local day, exactly like the total line this sits under.
        //
        // entry_kind = 'sale' is what makes the two agree to the cent: the view's synthesized rows
        // (rental deposits in Part 2, gift-card sales in Part 3) carry their own entry kinds and
        // drop out here, leaving precisely the ledger sale rows ReportsRepository.GetDailyRevenue
        // sums. Refunds are excluded there too, so both are gross.
        public async Task<List<DailyRevenueBucketRow>> GetDailyRevenueBuckets(Guid tenantId, DateTime fromUtc, DateTime toUtc)
        {
            const string sql = @"
                SELECT to_char(business_date, 'YYYY-MM-DD')          AS Date,
                       source_kind                                   AS SourceKind,
                       revenue_key_override                          AS RevenueKeyOverride,
                       COALESCE(SUM(gross_cents), 0)::bigint         AS GrossCents
                FROM v_accounting_entries
                WHERE tenant_id = @tenantId
                  AND entry_kind = 'sale'
                  AND occurred_at_utc >= @fromUtc AND occurred_at_utc < @toUtc
                GROUP BY business_date, source_kind, revenue_key_override
                ORDER BY business_date";
            return (await _db.Query<DailyRevenueBucketRow>(sql, new { tenantId, fromUtc, toUtc })).ToList();
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
