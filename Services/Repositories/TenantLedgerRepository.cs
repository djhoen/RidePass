using Services.Helpers.Interfaces;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class TenantLedgerRepository : ITenantLedgerRepository
    {
        private const string EntryColumns = @"
            id, tenant_id AS TenantId, entry_kind AS EntryKind,
            source_kind AS SourceKind, source_id AS SourceId,
            occurred_at_utc AS OccurredAtUtc,
            gross_cents AS GrossCents, stripe_fee_cents AS StripeFeeCents,
            ridepass_cut_cents AS RidepassCutCents, net_to_tenant_cents AS NetToTenantCents,
            applied_tier_id AS AppliedTierId,
            cumulative_monthly_volume_at_sale_cents AS CumulativeMonthlyVolumeAtSaleCents,
            stripe_payment_intent_id AS StripePaymentIntentId,
            payout_id AS PayoutId, memo,
            payment_method AS PaymentMethod,
            sold_by_user_id AS SoldByUserId,
            created_at AS CreatedAt";

        private readonly IDbHelper _db;

        public TenantLedgerRepository(IDbHelper db) => _db = db;

        public async Task<Guid> Insert(TenantLedgerEntry entry)
        {
            const string sql = @"
                INSERT INTO tenant_ledger_entry
                    (tenant_id, entry_kind, source_kind, source_id, occurred_at_utc,
                     gross_cents, stripe_fee_cents, ridepass_cut_cents, net_to_tenant_cents,
                     applied_tier_id, cumulative_monthly_volume_at_sale_cents,
                     stripe_payment_intent_id, payout_id, memo, payment_method, sold_by_user_id)
                VALUES
                    (@TenantId, @EntryKind, @SourceKind, @SourceId, @OccurredAtUtc,
                     @GrossCents, @StripeFeeCents, @RidepassCutCents, @NetToTenantCents,
                     @AppliedTierId, @CumulativeMonthlyVolumeAtSaleCents,
                     @StripePaymentIntentId, @PayoutId, @Memo, @PaymentMethod, @SoldByUserId)
                RETURNING id";
            return (await _db.Query<Guid>(sql, entry)).First();
        }

        public async Task<long> SumCashNetForWorker(Guid tenantId, Guid workerUserId, DateTime fromUtc, DateTime toUtc)
        {
            // Net cash the worker handled in the window: 'sale' rows are positive and 'refund'
            // rows carry negative gross, so the sum is sales minus refunds. Cash tender only.
            const string sql = @"
                SELECT COALESCE(SUM(gross_cents), 0)::bigint
                FROM tenant_ledger_entry
                WHERE tenant_id = @tenantId
                  AND sold_by_user_id = @workerUserId
                  AND payment_method = 'cash'
                  AND entry_kind IN ('sale', 'refund')
                  AND occurred_at_utc >= @fromUtc
                  AND occurred_at_utc < @toUtc";
            return (await _db.Query<long>(sql, new { tenantId, workerUserId, fromUtc, toUtc })).FirstOrDefault();
        }

        public async Task<List<WorkerRefundTotals>> ListRefundsByWorker(Guid tenantId, DateTime fromUtc, DateTime toUtc)
        {
            // Refund volume per worker over a window, split by tender. gross_cents is negative
            // on a refund, so -gross_cents is the positive amount returned. Card = stripe rails.
            const string sql = @"
                SELECT sold_by_user_id AS WorkerUserId,
                       COUNT(*) FILTER (WHERE payment_method = 'cash')                                       AS CashCount,
                       COALESCE(SUM(-gross_cents) FILTER (WHERE payment_method = 'cash'), 0)::bigint          AS CashCents,
                       COUNT(*) FILTER (WHERE payment_method IN ('stripe', 'stripe_connect'))                AS CardCount,
                       COALESCE(SUM(-gross_cents) FILTER (WHERE payment_method IN ('stripe', 'stripe_connect')), 0)::bigint AS CardCents
                FROM tenant_ledger_entry
                WHERE tenant_id = @tenantId
                  AND entry_kind = 'refund'
                  AND sold_by_user_id IS NOT NULL
                  AND occurred_at_utc >= @fromUtc
                  AND occurred_at_utc < @toUtc
                GROUP BY sold_by_user_id";
            return (await _db.Query<WorkerRefundTotals>(sql, new { tenantId, fromUtc, toUtc })).ToList();
        }

        public async Task<List<TenantLedgerEntry>> ListByTenant(Guid tenantId, DateTime? fromUtc, DateTime? toUtc, int take = 200)
        {
            var sql = $@"
                SELECT {EntryColumns}
                FROM tenant_ledger_entry
                WHERE tenant_id = @tenantId
                  AND (@fromUtc::timestamptz IS NULL OR occurred_at_utc >= @fromUtc)
                  AND (@toUtc::timestamptz IS NULL OR occurred_at_utc < @toUtc)
                ORDER BY occurred_at_utc DESC
                LIMIT @take";
            return (await _db.Query<TenantLedgerEntry>(sql, new { tenantId, fromUtc, toUtc, take })).ToList();
        }

        public async Task<long> GetMonthlyGrossVolumeCents(Guid tenantId, DateTime atUtc)
        {
            var monthStart = new DateTime(atUtc.Year, atUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            const string sql = @"
                SELECT COALESCE(SUM(gross_cents), 0)::bigint
                FROM tenant_ledger_entry
                WHERE tenant_id = @tenantId
                  AND entry_kind = 'sale'
                  AND occurred_at_utc >= @monthStart
                  AND occurred_at_utc < @atUtc";
            return (await _db.Query<long>(sql, new { tenantId, monthStart, atUtc })).FirstOrDefault();
        }

        public async Task<TenantLedgerEntry?> GetSaleEntryForSource(Guid tenantId, string sourceKind, Guid sourceId)
        {
            var sql = $@"
                SELECT {EntryColumns}
                FROM tenant_ledger_entry
                WHERE tenant_id = @tenantId
                  AND source_kind = @sourceKind
                  AND source_id = @sourceId
                  AND entry_kind = 'sale'
                LIMIT 1";
            return (await _db.Query<TenantLedgerEntry>(sql, new { tenantId, sourceKind, sourceId })).FirstOrDefault();
        }

        public async Task<LedgerPeriodTotals> SumForPeriod(DateTime fromUtc, DateTime toUtc)
        {
            // Reconciliation against Stripe is only meaningful for entries Stripe actually
            // processed. Cash and voucher rows are excluded.
            const string sql = @"
                SELECT
                    COUNT(*)::int AS Count,
                    COALESCE(SUM(gross_cents), 0)::bigint AS GrossCents,
                    COALESCE(SUM(stripe_fee_cents), 0)::bigint AS StripeFeeCents,
                    COALESCE(SUM(ridepass_cut_cents), 0)::bigint AS RidepassCutCents,
                    COALESCE(SUM(net_to_tenant_cents), 0)::bigint AS NetToTenantCents
                FROM tenant_ledger_entry
                WHERE occurred_at_utc >= @fromUtc AND occurred_at_utc < @toUtc
                  AND payment_method = 'stripe'";
            return (await _db.Query<LedgerPeriodTotals>(sql, new { fromUtc, toUtc })).First();
        }

        public async Task<long> GetMonthlyRidepassCutCents(Guid tenantId, DateTime atUtc)
        {
            var monthStart = new DateTime(atUtc.Year, atUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            const string sql = @"
                SELECT COALESCE(SUM(ridepass_cut_cents), 0)::bigint
                FROM tenant_ledger_entry
                WHERE tenant_id = @tenantId
                  AND occurred_at_utc >= @monthStart
                  AND occurred_at_utc < @atUtc";
            return (await _db.Query<long>(sql, new { tenantId, monthStart, atUtc })).FirstOrDefault();
        }

        public async Task<TenantBalanceSummary?> GetSummary(Guid tenantId)
        {
            var rows = await GetSummaries(new[] { tenantId });
            return rows.FirstOrDefault();
        }

        public async Task<List<TenantBalanceSummary>> GetSummariesForAllTenants()
        {
            return await GetSummaries(null);
        }

        private async Task<List<TenantBalanceSummary>> GetSummaries(IEnumerable<Guid>? tenantIds)
        {
            var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var sql = @"
                SELECT
                    t.id AS TenantId,
                    t.subdomain AS TenantSubdomain,
                    t.display_name AS TenantDisplayName,
                    COALESCE(SUM(CASE WHEN l.payout_id IS NULL THEN l.net_to_tenant_cents ELSE 0 END), 0)::int AS AvailableBalanceCents,
                    COALESCE(SUM(CASE WHEN l.entry_kind = 'sale' THEN l.gross_cents ELSE 0 END), 0)::int AS LifetimeGrossCents,
                    COALESCE(SUM(l.stripe_fee_cents), 0)::int AS LifetimeStripeFeeCents,
                    COALESCE(SUM(l.ridepass_cut_cents), 0)::int AS LifetimeRidepassCutCents,
                    COALESCE(SUM(CASE WHEN l.payout_id IS NOT NULL THEN l.net_to_tenant_cents ELSE 0 END), 0)::int AS LifetimePaidOutCents,
                    COALESCE(SUM(CASE WHEN l.entry_kind = 'sale' AND l.occurred_at_utc >= @monthStart THEN l.gross_cents ELSE 0 END), 0)::int AS CurrentMonthGrossCents
                FROM tenant t
                LEFT JOIN tenant_ledger_entry l ON l.tenant_id = t.id
                WHERE (@tenantIds::uuid[] IS NULL OR t.id = ANY(@tenantIds))
                GROUP BY t.id, t.subdomain, t.display_name
                ORDER BY t.subdomain";
            return (await _db.Query<TenantBalanceSummary>(sql, new { monthStart, tenantIds = tenantIds?.ToArray() })).ToList();
        }
    }
}
