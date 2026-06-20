using Services.Helpers.Interfaces;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class EventTicketTierRepository : IEventTicketTierRepository
    {
        private const string Columns = @"
            id, tenant_id AS TenantId, event_id AS EventId, kind, audience, required, name,
            price_cents AS PriceCents, inventory, sort_order AS SortOrder,
            is_active AS IsActive,
            ladder_group AS LadderGroup, min_sold AS MinSold,
            effective_days_before AS EffectiveDaysBefore, effective_at_utc AS EffectiveAtUtc,
            rider_paid_service_charge_bps AS RiderPaidServiceChargeBps,
            bundled_coupon_count AS BundledCouponCount,
            bundled_coupon_discount_kind AS BundledCouponDiscountKind,
            bundled_coupon_discount_value AS BundledCouponDiscountValue,
            bundled_coupon_scope AS BundledCouponScope,
            bundled_coupon_expires_in_days AS BundledCouponExpiresInDays,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

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
                INSERT INTO event_ticket_tier (
                    tenant_id, event_id, kind, audience, required, name, price_cents, inventory, sort_order, is_active,
                    ladder_group, min_sold, effective_days_before, effective_at_utc,
                    rider_paid_service_charge_bps,
                    bundled_coupon_count, bundled_coupon_discount_kind, bundled_coupon_discount_value,
                    bundled_coupon_scope, bundled_coupon_expires_in_days)
                VALUES (
                    @TenantId, @EventId, @Kind, @Audience, @Required, @Name, @PriceCents, @Inventory, @SortOrder, @IsActive,
                    @LadderGroup, @MinSold, @EffectiveDaysBefore, @EffectiveAtUtc,
                    @RiderPaidServiceChargeBps,
                    @BundledCouponCount, @BundledCouponDiscountKind, @BundledCouponDiscountValue,
                    @BundledCouponScope, @BundledCouponExpiresInDays)
                RETURNING id";
            var result = await _db.Query<Guid>(sql, t);
            return result.First();
        }

        public async Task Update(EventTicketTier t)
        {
            const string sql = @"
                UPDATE event_ticket_tier
                SET kind = @Kind, audience = @Audience, required = @Required,
                    name = @Name, price_cents = @PriceCents, inventory = @Inventory,
                    sort_order = @SortOrder, is_active = @IsActive,
                    ladder_group = @LadderGroup, min_sold = @MinSold,
                    effective_days_before = @EffectiveDaysBefore, effective_at_utc = @EffectiveAtUtc,
                    rider_paid_service_charge_bps = @RiderPaidServiceChargeBps,
                    bundled_coupon_count = @BundledCouponCount,
                    bundled_coupon_discount_kind = @BundledCouponDiscountKind,
                    bundled_coupon_discount_value = @BundledCouponDiscountValue,
                    bundled_coupon_scope = @BundledCouponScope,
                    bundled_coupon_expires_in_days = @BundledCouponExpiresInDays
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

        // Cumulative active sales across every step in one event's price ladder. Drives the
        // quantity trigger (a step fires when group sold reaches its min_sold). Joins through
        // event_ticket_tier so it's scoped by event + group; tenant-scoped via the purchase row.
        public async Task<int> GroupSoldCount(Guid eventId, string ladderGroup, Guid tenantId)
        {
            const string sql = @"
                SELECT COUNT(*)
                FROM event_ticket_purchase p
                JOIN event_ticket_tier t ON t.id = p.tier_id
                WHERE p.tenant_id = @tenantId
                  AND t.event_id = @eventId
                  AND t.ladder_group = @ladderGroup
                  AND p.status IN ('pending', 'paid', 'redeemed')";
            return await _db.ExecuteScalar(sql, new { eventId, ladderGroup, tenantId });
        }

        public async Task UpdateSortOrders(Guid tenantId, Guid eventId, IReadOnlyList<Guid> ids, IReadOnlyList<int> sortOrders)
        {
            if (ids.Count == 0) return;
            // Constrain by both tenant_id (security) and event_id (a UI bug can't
            // move a tier across events even if its id leaked into the request).
            const string sql = @"
                UPDATE event_ticket_tier AS t
                SET sort_order = data.sort_order, updated_at = now()
                FROM (SELECT unnest(@ids::uuid[]) AS id,
                             unnest(@orders::int[]) AS sort_order) AS data
                WHERE t.id = data.id AND t.tenant_id = @tenantId AND t.event_id = @eventId";
            await _db.Execute(sql, new
            {
                tenantId,
                eventId,
                ids = ids.ToArray(),
                orders = sortOrders.ToArray(),
            });
        }
    }
}
