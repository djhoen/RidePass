using Services.Helpers.Interfaces;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class TenantPayoutRepository : ITenantPayoutRepository
    {
        private const string PayoutColumns = @"
            id, tenant_id AS TenantId, status,
            period_start_utc AS PeriodStartUtc, period_end_utc AS PeriodEndUtc,
            payout_date_utc AS PayoutDateUtc,
            total_gross_cents AS TotalGrossCents, total_stripe_fee_cents AS TotalStripeFeeCents,
            total_ridepass_cut_cents AS TotalRidepassCutCents, total_adjustment_cents AS TotalAdjustmentCents,
            net_paid_cents AS NetPaidCents,
            external_reference AS ExternalReference, memo,
            created_by_user_id AS CreatedByUserId, approved_by_user_id AS ApprovedByUserId,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        private const string LedgerColumns = @"
            id, tenant_id AS TenantId, entry_kind AS EntryKind,
            source_kind AS SourceKind, source_id AS SourceId,
            occurred_at_utc AS OccurredAtUtc,
            gross_cents AS GrossCents, stripe_fee_cents AS StripeFeeCents,
            ridepass_cut_cents AS RidepassCutCents, net_to_tenant_cents AS NetToTenantCents,
            applied_tier_id AS AppliedTierId,
            cumulative_monthly_volume_at_sale_cents AS CumulativeMonthlyVolumeAtSaleCents,
            stripe_payment_intent_id AS StripePaymentIntentId,
            payout_id AS PayoutId, memo, created_at AS CreatedAt";

        private readonly IDbHelper _db;

        public TenantPayoutRepository(IDbHelper db) => _db = db;

        public async Task<List<TenantPayout>> ListByTenant(Guid tenantId, int take = 50)
        {
            var sql = $@"
                SELECT {PayoutColumns}
                FROM tenant_payout
                WHERE tenant_id = @tenantId
                ORDER BY period_start_utc DESC
                LIMIT @take";
            return (await _db.Query<TenantPayout>(sql, new { tenantId, take })).ToList();
        }

        public async Task<TenantPayout?> GetById(Guid id, Guid tenantId)
        {
            var sql = $@"
                SELECT {PayoutColumns}
                FROM tenant_payout
                WHERE id = @id AND tenant_id = @tenantId
                LIMIT 1";
            return (await _db.Query<TenantPayout>(sql, new { id, tenantId })).FirstOrDefault();
        }

        public async Task<List<TenantLedgerEntry>> ListEntriesForPayout(Guid payoutId)
        {
            var sql = $@"
                SELECT {LedgerColumns}
                FROM tenant_ledger_entry
                WHERE payout_id = @payoutId
                ORDER BY occurred_at_utc";
            return (await _db.Query<TenantLedgerEntry>(sql, new { payoutId })).ToList();
        }

        public async Task<Guid> Create(TenantPayout payout)
        {
            const string sql = @"
                INSERT INTO tenant_payout
                    (tenant_id, status, period_start_utc, period_end_utc, payout_date_utc,
                     total_gross_cents, total_stripe_fee_cents, total_ridepass_cut_cents,
                     total_adjustment_cents, net_paid_cents,
                     external_reference, memo, created_by_user_id, approved_by_user_id)
                VALUES
                    (@TenantId, @Status, @PeriodStartUtc, @PeriodEndUtc, @PayoutDateUtc,
                     @TotalGrossCents, @TotalStripeFeeCents, @TotalRidepassCutCents,
                     @TotalAdjustmentCents, @NetPaidCents,
                     @ExternalReference, @Memo, @CreatedByUserId, @ApprovedByUserId)
                RETURNING id";
            return (await _db.Query<Guid>(sql, payout)).First();
        }

        public async Task<int> AttachUnpaidEntries(Guid payoutId, Guid tenantId, DateTime fromUtc, DateTime toUtc)
        {
            const string sql = @"
                UPDATE tenant_ledger_entry
                SET payout_id = @payoutId
                WHERE tenant_id = @tenantId
                  AND payout_id IS NULL
                  AND occurred_at_utc >= @fromUtc
                  AND occurred_at_utc < @toUtc";
            return await _db.Execute(sql, new { payoutId, tenantId, fromUtc, toUtc });
        }

        public async Task RefreshTotals(Guid payoutId)
        {
            const string sql = @"
                UPDATE tenant_payout p
                SET total_gross_cents = COALESCE(s.total_gross, 0),
                    total_stripe_fee_cents = COALESCE(s.total_stripe, 0),
                    total_ridepass_cut_cents = COALESCE(s.total_cut, 0),
                    total_adjustment_cents = COALESCE(s.total_adjustments, 0),
                    net_paid_cents = COALESCE(s.total_net, 0)
                FROM (
                    SELECT
                        SUM(CASE WHEN entry_kind = 'sale' THEN gross_cents ELSE 0 END)::int AS total_gross,
                        SUM(stripe_fee_cents)::int AS total_stripe,
                        SUM(ridepass_cut_cents)::int AS total_cut,
                        SUM(CASE WHEN entry_kind <> 'sale' THEN gross_cents ELSE 0 END)::int AS total_adjustments,
                        SUM(net_to_tenant_cents)::int AS total_net
                    FROM tenant_ledger_entry
                    WHERE payout_id = @payoutId
                ) s
                WHERE p.id = @payoutId";
            await _db.Execute(sql, new { payoutId });
        }

        public async Task UpdateStatus(Guid id, Guid tenantId, string status, DateTime? payoutDateUtc, string? externalReference, string? memo, Guid? approvedByUserId)
        {
            const string sql = @"
                UPDATE tenant_payout
                SET status = @status,
                    payout_date_utc = COALESCE(@payoutDateUtc, payout_date_utc),
                    external_reference = COALESCE(@externalReference, external_reference),
                    memo = COALESCE(@memo, memo),
                    approved_by_user_id = COALESCE(@approvedByUserId, approved_by_user_id)
                WHERE id = @id AND tenant_id = @tenantId";
            await _db.Execute(sql, new { id, tenantId, status, payoutDateUtc, externalReference, memo, approvedByUserId });
        }

        public async Task<bool> Void(Guid payoutId, Guid tenantId)
        {
            // Detach ledger entries first so they become unpaid again.
            await _db.Execute(@"
                UPDATE tenant_ledger_entry SET payout_id = NULL WHERE payout_id = @payoutId",
                new { payoutId });

            var rows = await _db.Execute(@"
                DELETE FROM tenant_payout WHERE id = @payoutId AND tenant_id = @tenantId AND status = 'pending'",
                new { payoutId, tenantId });
            return rows > 0;
        }
    }
}
