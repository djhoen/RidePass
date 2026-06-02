using Services.Helpers.Interfaces;
using Services.Repositories.Data.BillingData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class TenantBillingEventRepository : ITenantBillingEventRepository
    {
        private const string SelectColumns = @"
            id, tenant_id AS TenantId, kind, source_table AS SourceTable,
            source_id AS SourceId, twilio_cost_micros AS TwilioCostMicros,
            billed_cents AS BilledCents, payout_entry_id AS PayoutEntryId,
            pushed_to_payout_at_utc AS PushedToPayoutAt, created_at_utc AS CreatedAt";

        private readonly IDbHelper _db;

        public TenantBillingEventRepository(IDbHelper db)
        {
            _db = db;
        }

        public async Task<bool> RecordIfNew(TenantBillingEvent ev)
        {
            // ON CONFLICT DO NOTHING + RETURNING id: rows inserted return their
            // id; duplicates return nothing. Empty result set => the unique
            // (kind, source_id) index rejected the insert => duplicate webhook,
            // already accounted for, no-op.
            const string sql = @"
                INSERT INTO tenant_billing_event
                    (tenant_id, kind, source_table, source_id, twilio_cost_micros, billed_cents)
                VALUES (@TenantId, @Kind, @SourceTable, @SourceId, @TwilioCostMicros, @BilledCents)
                ON CONFLICT (kind, source_id) DO NOTHING
                RETURNING id";
            var result = await _db.Query<Guid>(sql, ev);
            return result.Any();
        }

        public async Task<List<TenantBillingEvent>> ListPendingPayoutAttach(int limit)
        {
            var sql = $@"
                SELECT {SelectColumns}
                FROM tenant_billing_event
                WHERE pushed_to_payout_at_utc IS NULL
                ORDER BY created_at_utc
                LIMIT @limit";
            var result = await _db.Query<TenantBillingEvent>(sql, new { limit });
            return result.ToList();
        }

        public async Task MarkAttachedToPayout(Guid id, Guid payoutEntryId)
        {
            const string sql = @"
                UPDATE tenant_billing_event
                SET payout_entry_id = @payoutEntryId,
                    pushed_to_payout_at_utc = now()
                WHERE id = @id";
            await _db.Execute(sql, new { id, payoutEntryId });
        }

        public async Task<int> SumBilledCents(Guid tenantId, DateTime fromUtc, DateTime toUtc, string? kind = null)
        {
            var sql = @"
                SELECT COALESCE(SUM(billed_cents), 0)::int
                FROM tenant_billing_event
                WHERE tenant_id = @tenantId
                  AND created_at_utc >= @fromUtc
                  AND created_at_utc < @toUtc";
            if (kind is not null) sql += " AND kind = @kind";
            var result = await _db.Query<int>(sql, new { tenantId, fromUtc, toUtc, kind });
            return result.FirstOrDefault();
        }
    }
}
