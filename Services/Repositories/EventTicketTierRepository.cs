using Services.Helpers.Interfaces;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class EventTicketTierRepository : IEventTicketTierRepository
    {
        private const string Columns = @"
            id, tenant_id AS TenantId, event_id AS EventId, name,
            price_cents AS PriceCents, inventory, sort_order AS SortOrder,
            is_active AS IsActive, created_at AS CreatedAt, updated_at AS UpdatedAt";

        private readonly IDbHelper _db;

        public EventTicketTierRepository(IDbHelper db) => _db = db;

        public async Task<List<EventTicketTier>> GetForEvent(Guid eventId, Guid tenantId, bool activeOnly)
        {
            var filter = activeOnly ? " AND is_active = true" : "";
            var sql = $@"
                SELECT {Columns}
                FROM event_ticket_tier
                WHERE event_id = @eventId AND tenant_id = @tenantId {filter}
                ORDER BY sort_order, name";
            var result = await _db.Query<EventTicketTier>(sql, new { eventId, tenantId });
            return result.ToList();
        }

        public async Task<Dictionary<Guid, List<EventTicketTier>>> GetForEvents(IEnumerable<Guid> eventIds, Guid tenantId, bool activeOnly)
        {
            var ids = eventIds.ToArray();
            if (ids.Length == 0) return new();
            var filter = activeOnly ? " AND is_active = true" : "";
            var sql = $@"
                SELECT {Columns}
                FROM event_ticket_tier
                WHERE event_id = ANY(@ids) AND tenant_id = @tenantId {filter}
                ORDER BY sort_order, name";
            var rows = await _db.Query<EventTicketTier>(sql, new { ids, tenantId });
            return rows.GroupBy(t => t.EventId).ToDictionary(g => g.Key, g => g.ToList());
        }

        public async Task<EventTicketTier?> GetById(Guid id, Guid tenantId)
        {
            var sql = $"SELECT {Columns} FROM event_ticket_tier WHERE id = @id AND tenant_id = @tenantId LIMIT 1";
            var result = await _db.Query<EventTicketTier>(sql, new { id, tenantId });
            return result.FirstOrDefault();
        }

        public async Task<Guid> Create(EventTicketTier t)
        {
            const string sql = @"
                INSERT INTO event_ticket_tier (tenant_id, event_id, name, price_cents, inventory, sort_order, is_active)
                VALUES (@TenantId, @EventId, @Name, @PriceCents, @Inventory, @SortOrder, @IsActive)
                RETURNING id";
            var result = await _db.Query<Guid>(sql, t);
            return result.First();
        }

        public async Task Update(EventTicketTier t)
        {
            const string sql = @"
                UPDATE event_ticket_tier
                SET name = @Name, price_cents = @PriceCents, inventory = @Inventory,
                    sort_order = @SortOrder, is_active = @IsActive
                WHERE id = @Id AND tenant_id = @TenantId";
            await _db.Execute(sql, t);
        }

        public async Task Delete(Guid id, Guid tenantId)
        {
            const string sql = "DELETE FROM event_ticket_tier WHERE id = @id AND tenant_id = @tenantId";
            await _db.Execute(sql, new { id, tenantId });
        }

        public async Task<int> SoldCount(Guid tierId)
        {
            const string sql = @"
                SELECT COUNT(*) FROM event_ticket_purchase
                WHERE tier_id = @tierId AND status IN ('pending', 'paid', 'redeemed')";
            return await _db.ExecuteScalar(sql, new { tierId });
        }
    }
}
