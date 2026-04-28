using Services.Helpers.Interfaces;
using Services.Repositories.Data.EventData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class EventRepository : IEventRepository
    {
        private const string SelectColumns = @"
            id, tenant_id AS TenantId, event_type_id AS EventTypeId,
            title, description,
            starts_at AS StartsAt, ends_at AS EndsAt, all_day AS AllDay,
            capacity, location_label AS LocationLabel, status,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        private readonly IDbHelper _db;

        public EventRepository(IDbHelper db)
        {
            _db = db;
        }

        public async Task<List<Event>> GetInRange(Guid tenantId, DateTime fromUtc, DateTime toUtc)
        {
            // Overlap test: event visible when its range intersects [fromUtc, toUtc].
            var sql = $@"
                SELECT {SelectColumns}
                FROM event
                WHERE tenant_id = @tenantId
                  AND starts_at < @toUtc
                  AND ends_at >= @fromUtc
                ORDER BY starts_at, id";
            var result = await _db.Query<Event>(sql, new { tenantId, fromUtc, toUtc });
            return result.ToList();
        }

        public async Task<Event?> GetById(Guid id, Guid tenantId)
        {
            var sql = $@"
                SELECT {SelectColumns}
                FROM event
                WHERE id = @id AND tenant_id = @tenantId
                LIMIT 1";
            var result = await _db.Query<Event>(sql, new { id, tenantId });
            return result.FirstOrDefault();
        }

        public async Task<Guid> Create(Event ev)
        {
            const string sql = @"
                INSERT INTO event (tenant_id, event_type_id, title, description,
                                   starts_at, ends_at, all_day, capacity, location_label, status)
                VALUES (@TenantId, @EventTypeId, @Title, @Description,
                        @StartsAt, @EndsAt, @AllDay, @Capacity, @LocationLabel, @Status)
                RETURNING id";
            var result = await _db.Query<Guid>(sql, ev);
            return result.First();
        }

        public async Task Update(Event ev)
        {
            const string sql = @"
                UPDATE event
                SET event_type_id  = @EventTypeId,
                    title          = @Title,
                    description    = @Description,
                    starts_at      = @StartsAt,
                    ends_at        = @EndsAt,
                    all_day        = @AllDay,
                    capacity       = @Capacity,
                    location_label = @LocationLabel,
                    status         = @Status
                WHERE id = @Id AND tenant_id = @TenantId";
            await _db.Execute(sql, ev);
        }

        public async Task Delete(Guid id, Guid tenantId)
        {
            const string sql = "DELETE FROM event WHERE id = @id AND tenant_id = @tenantId";
            await _db.Execute(sql, new { id, tenantId });
        }

        public async Task<List<EventWithTypeContext>> GetUpcomingWithType(Guid tenantId, int limit)
        {
            const string sql = @"
                SELECT e.id, e.tenant_id AS TenantId, e.event_type_id AS EventTypeId,
                       e.title, e.description,
                       e.starts_at AS StartsAt, e.ends_at AS EndsAt, e.all_day AS AllDay,
                       e.capacity, e.location_label AS LocationLabel, e.status,
                       e.created_at AS CreatedAt, e.updated_at AS UpdatedAt,
                       et.name AS EventTypeName, et.color AS EventTypeColor
                FROM event e
                JOIN tenant_event_type et ON et.id = e.event_type_id
                WHERE e.tenant_id = @tenantId
                  AND e.status = 'scheduled'
                  AND e.starts_at >= NOW()
                ORDER BY e.starts_at
                LIMIT @limit";
            var result = await _db.Query<EventWithTypeContext>(sql, new { tenantId, limit });
            return result.ToList();
        }
    }
}
