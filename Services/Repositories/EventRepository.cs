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
            requires_rider_waiver AS RequiresRiderWaiver,
            requires_spectator_waiver AS RequiresSpectatorWaiver,
            spectator_waiver_id AS SpectatorWaiverId,
            racer_waiver_id AS RacerWaiverId,
            image_url AS ImageUrl,
            schedule_json::text AS ScheduleJson,
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
                                   starts_at, ends_at, all_day, capacity, location_label, status,
                                   requires_rider_waiver, requires_spectator_waiver,
                                   spectator_waiver_id, racer_waiver_id, image_url, schedule_json)
                VALUES (@TenantId, @EventTypeId, @Title, @Description,
                        @StartsAt, @EndsAt, @AllDay, @Capacity, @LocationLabel, @Status,
                        @RequiresRiderWaiver, @RequiresSpectatorWaiver,
                        @SpectatorWaiverId, @RacerWaiverId, @ImageUrl,
                        COALESCE(@ScheduleJson::jsonb, '[]'::jsonb))
                RETURNING id";
            var result = await _db.Query<Guid>(sql, ev);
            return result.First();
        }

        public async Task Update(Event ev)
        {
            const string sql = @"
                UPDATE event
                SET event_type_id   = @EventTypeId,
                    title           = @Title,
                    description     = @Description,
                    starts_at       = @StartsAt,
                    ends_at         = @EndsAt,
                    all_day         = @AllDay,
                    capacity        = @Capacity,
                    location_label  = @LocationLabel,
                    status          = @Status,
                    requires_rider_waiver     = @RequiresRiderWaiver,
                    requires_spectator_waiver = @RequiresSpectatorWaiver,
                    spectator_waiver_id = @SpectatorWaiverId,
                    racer_waiver_id     = @RacerWaiverId,
                    image_url           = @ImageUrl,
                    schedule_json       = COALESCE(@ScheduleJson::jsonb, '[]'::jsonb)
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
                       e.requires_rider_waiver AS RequiresRiderWaiver,
                       e.requires_spectator_waiver AS RequiresSpectatorWaiver,
                       e.spectator_waiver_id AS SpectatorWaiverId,
                       e.racer_waiver_id AS RacerWaiverId,
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

        public async Task<List<EventWaiverAssociation>> ListByWaiverId(Guid waiverId, Guid tenantId)
        {
            const string sql = @"
                SELECT id, title, starts_at AS StartsAt, ends_at AS EndsAt,
                       (racer_waiver_id     = @waiverId) AS AsRider,
                       (spectator_waiver_id = @waiverId) AS AsSpectator
                FROM event
                WHERE tenant_id = @tenantId
                  AND (racer_waiver_id = @waiverId OR spectator_waiver_id = @waiverId)
                ORDER BY starts_at, id";
            var result = await _db.Query<EventWaiverAssociation>(sql, new { waiverId, tenantId });
            return result.ToList();
        }

        public async Task SetWaiverRole(Guid eventId, Guid tenantId, Guid waiverId, bool asRider, bool asSpectator)
        {
            // One UPDATE: each column flips to @waiverId when its role is requested,
            // or NULLs out (only if it currently points at @waiverId — so we don't
            // wipe out a different waiver that another admin attached). The
            // requires_*_waiver flags flip true whenever we set the corresponding
            // column, but we don't flip them false on detach (admin may still want
            // a default-fallback waiver applied).
            const string sql = @"
                UPDATE event SET
                    racer_waiver_id = CASE
                        WHEN @asRider THEN @waiverId
                        WHEN racer_waiver_id = @waiverId THEN NULL
                        ELSE racer_waiver_id
                    END,
                    spectator_waiver_id = CASE
                        WHEN @asSpectator THEN @waiverId
                        WHEN spectator_waiver_id = @waiverId THEN NULL
                        ELSE spectator_waiver_id
                    END,
                    requires_rider_waiver = CASE
                        WHEN @asRider THEN TRUE
                        ELSE requires_rider_waiver
                    END,
                    requires_spectator_waiver = CASE
                        WHEN @asSpectator THEN TRUE
                        ELSE requires_spectator_waiver
                    END
                WHERE id = @eventId AND tenant_id = @tenantId";
            await _db.Execute(sql, new { eventId, tenantId, waiverId, asRider, asSpectator });
        }

        public async Task<List<Guid>> ListEligiblePassProductIds(Guid eventId)
        {
            const string sql = @"
                SELECT pass_product_id FROM event_pass_eligibility
                WHERE event_id = @eventId";
            return (await _db.Query<Guid>(sql, new { eventId })).ToList();
        }

        public async Task<Dictionary<Guid, List<Guid>>> ListEligibilityForEvents(IEnumerable<Guid> eventIds)
        {
            var ids = eventIds.ToArray();
            if (ids.Length == 0) return new Dictionary<Guid, List<Guid>>();
            const string sql = @"
                SELECT event_id AS EventId, pass_product_id AS ProductId
                FROM event_pass_eligibility
                WHERE event_id = ANY(@ids)";
            var rows = await _db.Query<EligibilityRow>(sql, new { ids });
            return rows
                .GroupBy(r => r.EventId)
                .ToDictionary(g => g.Key, g => g.Select(r => r.ProductId).ToList());
        }

        private record EligibilityRow(Guid EventId, Guid ProductId);

        public async Task ReplacePassEligibility(Guid eventId, IEnumerable<Guid> productIds)
        {
            // Wipe + insert. Done in two statements; the join table has no FKs out
            // of these two cascades so a momentary empty state is fine for MVP.
            await _db.Execute("DELETE FROM event_pass_eligibility WHERE event_id = @eventId",
                new { eventId });
            const string insert = @"
                INSERT INTO event_pass_eligibility (event_id, pass_product_id)
                VALUES (@eventId, @productId)
                ON CONFLICT DO NOTHING";
            foreach (var pid in productIds.Distinct())
            {
                await _db.Execute(insert, new { eventId, productId = pid });
            }
        }

        public async Task<bool> IsPassProductEligible(Guid eventId, Guid productId)
        {
            const string sql = @"
                SELECT COUNT(*) FROM event_pass_eligibility
                WHERE event_id = @eventId AND pass_product_id = @productId";
            var count = await _db.ExecuteScalar(sql, new { eventId, productId });
            return count > 0;
        }
    }
}
