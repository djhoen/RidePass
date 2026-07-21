using Services.Helpers.Interfaces;
using Services.Repositories.Data.InstructorData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class InstructorRepository : IInstructorRepository
    {
        private const string Columns = @"
            id, tenant_id AS TenantId, name, email, phone, bio,
            image_url AS ImageUrl, is_active AS IsActive, sort_order AS SortOrder,
            max_students_per_session AS MaxStudentsPerSession,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        private readonly IDbHelper _db;
        public InstructorRepository(IDbHelper db) => _db = db;

        public async Task<List<Instructor>> List(Guid tenantId, bool activeOnly)
        {
            var where = activeOnly ? "AND is_active = true" : "";
            var sql = $"SELECT {Columns} FROM instructor " +
                      $"WHERE tenant_id = @tenantId {where} ORDER BY sort_order, name";
            return (await _db.Query<Instructor>(sql, new { tenantId })).ToList();
        }

        public async Task<Instructor?> Get(Guid id, Guid tenantId)
        {
            var sql = $"SELECT {Columns} FROM instructor WHERE id = @id AND tenant_id = @tenantId LIMIT 1";
            return (await _db.Query<Instructor>(sql, new { id, tenantId })).FirstOrDefault();
        }

        public async Task<Guid> Create(Instructor i)
        {
            const string sql = @"
                INSERT INTO instructor
                    (tenant_id, name, email, phone, bio, image_url, is_active, sort_order,
                     max_students_per_session)
                VALUES
                    (@TenantId, @Name, @Email, @Phone, @Bio, @ImageUrl, @IsActive, @SortOrder,
                     @MaxStudentsPerSession)
                RETURNING id";
            return (await _db.Query<Guid>(sql, i)).First();
        }

        public async Task Update(Instructor i)
        {
            const string sql = @"
                UPDATE instructor SET
                    name = @Name, email = @Email, phone = @Phone, bio = @Bio,
                    image_url = @ImageUrl, is_active = @IsActive, sort_order = @SortOrder,
                    max_students_per_session = @MaxStudentsPerSession
                WHERE id = @Id AND tenant_id = @TenantId";
            await _db.Execute(sql, i);
        }

        public async Task Delete(Guid id, Guid tenantId)
        {
            // FK from event_instructor is ON DELETE RESTRICT, so a delete only succeeds
            // when the instructor has no lessons on the books. Caller catches 23503 and
            // steers the admin to deactivate instead.
            const string sql = "DELETE FROM instructor WHERE id = @id AND tenant_id = @tenantId";
            await _db.Execute(sql, new { id, tenantId });
        }

        // ── Event assignment ─────────────────────────────────────────────────
        public async Task<List<Instructor>> ListForEvent(Guid eventId, Guid tenantId)
        {
            // Tenant-scoped on the instructor row: event_instructor carries no tenant column, so
            // without this a crafted event id could surface another tenant's coaches.
            var sql = $@"
                SELECT {Columns} FROM instructor i
                JOIN event_instructor ei ON ei.instructor_id = i.id
                WHERE ei.event_id = @eventId AND i.tenant_id = @tenantId
                ORDER BY i.sort_order, i.name";
            return (await _db.Query<Instructor>(sql, new { eventId, tenantId })).ToList();
        }

        public async Task<Dictionary<Guid, List<Guid>>> ListAssignmentsForEvents(IEnumerable<Guid> eventIds)
        {
            var ids = eventIds.ToArray();
            var result = new Dictionary<Guid, List<Guid>>();
            if (ids.Length == 0) return result;

            const string sql = @"
                SELECT event_id AS EventId, instructor_id AS InstructorId
                FROM event_instructor
                WHERE event_id = ANY(@ids)";
            var rows = await _db.Query<(Guid EventId, Guid InstructorId)>(sql, new { ids });
            foreach (var row in rows)
            {
                if (!result.TryGetValue(row.EventId, out var list))
                {
                    list = new List<Guid>();
                    result[row.EventId] = list;
                }
                list.Add(row.InstructorId);
            }
            return result;
        }

        public async Task ReplaceEventInstructors(Guid eventId, IEnumerable<Guid> instructorIds)
        {
            // Delete-then-insert the full set. Small N (a lesson has a handful of coaches),
            // so a straightforward replace is fine.
            await _db.Execute("DELETE FROM event_instructor WHERE event_id = @eventId", new { eventId });
            const string ins = @"
                INSERT INTO event_instructor (event_id, instructor_id)
                VALUES (@eventId, @instructorId)
                ON CONFLICT DO NOTHING";
            foreach (var instructorId in instructorIds.Distinct())
            {
                await _db.Execute(ins, new { eventId, instructorId });
            }
        }

        public async Task<List<InstructorConflict>> FindConflicts(
            Guid tenantId, IReadOnlyList<Guid> instructorIds,
            DateTime startsAt, DateTime endsAt, Guid? excludeEventId)
        {
            if (instructorIds.Count == 0) return new List<InstructorConflict>();

            // Half-open overlap: e.starts_at < @endsAt AND e.ends_at > @startsAt. A lesson
            // ending exactly when another begins does NOT clash. Cancelled events are ignored.
            const string sql = @"
                SELECT ei.instructor_id AS InstructorId,
                       i.name           AS InstructorName,
                       e.id             AS EventId,
                       e.title          AS EventTitle,
                       e.starts_at      AS StartsAt,
                       e.ends_at        AS EndsAt
                FROM event_instructor ei
                JOIN event e      ON e.id = ei.event_id
                JOIN instructor i ON i.id = ei.instructor_id
                WHERE e.tenant_id = @tenantId
                  AND ei.instructor_id = ANY(@instructorIds)
                  AND e.status = 'scheduled'
                  AND (@excludeEventId::uuid IS NULL OR e.id <> @excludeEventId)
                  AND e.starts_at < @endsAt
                  AND e.ends_at   > @startsAt
                ORDER BY e.starts_at";
            return (await _db.Query<InstructorConflict>(sql, new
            {
                tenantId,
                instructorIds = instructorIds.ToArray(),
                startsAt,
                endsAt,
                excludeEventId,
            })).ToList();
        }
    }
}
