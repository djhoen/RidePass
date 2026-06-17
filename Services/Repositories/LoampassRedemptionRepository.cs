using Services.Helpers.Interfaces;
using Services.Repositories.Data.UserData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class LoampassRedemptionRepository : ILoampassRedemptionRepository
    {
        private const string SelectColumns = @"
            id, tenant_id AS TenantId, event_ticket_purchase_id AS EventTicketPurchaseId,
            loampass_account_id AS LoampassAccountId, destination_id AS DestinationId,
            idempotency_key AS IdempotencyKey, status, created_at AS CreatedAt, refunded_at AS RefundedAt";

        private readonly IDbHelper _db;

        public LoampassRedemptionRepository(IDbHelper db)
        {
            _db = db;
        }

        public async Task Create(LoampassRedemption redemption)
        {
            const string sql = @"
                INSERT INTO loampass_redemption
                    (tenant_id, event_ticket_purchase_id, loampass_account_id, destination_id, idempotency_key, status)
                VALUES (@TenantId, @EventTicketPurchaseId, @LoampassAccountId, @DestinationId, @IdempotencyKey, @Status)";
            await _db.Execute(sql, redemption);
        }

        public async Task<LoampassRedemption?> GetByPurchaseId(Guid eventTicketPurchaseId, Guid tenantId)
        {
            var sql = $@"
                SELECT {SelectColumns}
                FROM loampass_redemption
                WHERE event_ticket_purchase_id = @eventTicketPurchaseId AND tenant_id = @tenantId
                LIMIT 1";
            var result = await _db.Query<LoampassRedemption>(sql, new { eventTicketPurchaseId, tenantId });
            return result.FirstOrDefault();
        }

        public async Task MarkRefunded(Guid id)
        {
            await _db.Execute(
                "UPDATE loampass_redemption SET status = 'refunded', refunded_at = now() WHERE id = @id",
                new { id });
        }
    }
}
