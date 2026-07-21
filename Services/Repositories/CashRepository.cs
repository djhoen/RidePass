using Services.Helpers.Interfaces;
using Services.Repositories.Data.CashData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class CashRepository : ICashRepository
    {
        private readonly IDbHelper _db;

        private const string SelectSessionColumns = @"
            id, tenant_id AS TenantId, event_id AS EventId, user_id AS UserId,
            device_id AS DeviceId, opening_float_cents AS OpeningFloatCents, status,
            opened_at AS OpenedAt, closed_at AS ClosedAt";

        private const string SelectTurnInColumns = @"
            id, tenant_id AS TenantId, cash_session_id AS CashSessionId, event_id AS EventId,
            worker_user_id AS WorkerUserId, manager_user_id AS ManagerUserId,
            expected_cents AS ExpectedCents, worker_counted_cents AS WorkerCountedCents,
            manager_counted_cents AS ManagerCountedCents, variance_cents AS VarianceCents,
            status, note, submitted_at AS SubmittedAt, confirmed_at AS ConfirmedAt";

        public CashRepository(IDbHelper db)
        {
            _db = db;
        }

        // ── Sessions ─────────────────────────────────────────────────────────────

        public async Task<CashSession?> GetOpenSession(Guid tenantId, Guid userId, Guid? eventId)
        {
            // event_id NULL and a concrete event are distinct sessions, so branch the
            // predicate rather than rely on null-equality of a parameter.
            var eventClause = eventId.HasValue ? "event_id = @eventId" : "event_id IS NULL";
            var sql = $@"
                SELECT {SelectSessionColumns}
                FROM cash_session
                WHERE tenant_id = @tenantId AND user_id = @userId AND status = 'open'
                  AND ({eventClause})
                LIMIT 1";
            var result = await _db.Query<CashSession>(sql, new { tenantId, userId, eventId });
            return result.FirstOrDefault();
        }

        public async Task<CashSession?> GetSessionById(Guid id, Guid tenantId)
        {
            var sql = $@"
                SELECT {SelectSessionColumns}
                FROM cash_session
                WHERE id = @id AND tenant_id = @tenantId
                LIMIT 1";
            var result = await _db.Query<CashSession>(sql, new { id, tenantId });
            return result.FirstOrDefault();
        }

        public async Task<List<CashSession>> ListSessionsByEvent(Guid tenantId, Guid eventId)
        {
            var sql = $@"
                SELECT {SelectSessionColumns}
                FROM cash_session
                WHERE tenant_id = @tenantId AND event_id = @eventId
                ORDER BY opened_at";
            var result = await _db.Query<CashSession>(sql, new { tenantId, eventId });
            return result.ToList();
        }

        // Sessions NOT tied to an event: a bike shop or F&B shift is a shift, not an event day.
        // Windowed by opened_at so the manager's view stays bounded.
        public async Task<List<CashSession>> ListSessionsWithoutEvent(Guid tenantId, DateTime fromUtc, DateTime toUtc)
        {
            var sql = $@"
                SELECT {SelectSessionColumns}
                FROM cash_session
                WHERE tenant_id = @tenantId AND event_id IS NULL
                  AND opened_at >= @fromUtc AND opened_at < @toUtc
                ORDER BY opened_at";
            return (await _db.Query<CashSession>(sql, new { tenantId, fromUtc, toUtc })).ToList();
        }

        public async Task<Guid> CreateSession(CashSession session)
        {
            const string sql = @"
                INSERT INTO cash_session (tenant_id, event_id, user_id, device_id, opening_float_cents, status)
                VALUES (@TenantId, @EventId, @UserId, @DeviceId, @OpeningFloatCents, @Status)
                RETURNING id";
            var result = await _db.Query<Guid>(sql, session);
            return result.First();
        }

        public async Task SetSessionStatus(Guid id, Guid tenantId, string status)
        {
            // closed_at stamps when the session leaves 'open'; reopening is not a flow.
            const string sql = @"
                UPDATE cash_session
                SET status = @status,
                    closed_at = CASE WHEN @status = 'open' THEN NULL ELSE now() END
                WHERE id = @id AND tenant_id = @tenantId";
            await _db.Execute(sql, new { id, tenantId, status });
        }

        // ── Turn-ins ─────────────────────────────────────────────────────────────

        public async Task<Guid> CreateTurnIn(CashTurnIn turnIn)
        {
            const string sql = @"
                INSERT INTO cash_turn_in (tenant_id, cash_session_id, event_id, worker_user_id,
                                          expected_cents, worker_counted_cents, status, note)
                VALUES (@TenantId, @CashSessionId, @EventId, @WorkerUserId,
                        @ExpectedCents, @WorkerCountedCents, @Status, @Note)
                RETURNING id";
            var result = await _db.Query<Guid>(sql, turnIn);
            return result.First();
        }

        public async Task<CashTurnIn?> GetTurnInById(Guid id, Guid tenantId)
        {
            var sql = $@"
                SELECT {SelectTurnInColumns}
                FROM cash_turn_in
                WHERE id = @id AND tenant_id = @tenantId
                LIMIT 1";
            var result = await _db.Query<CashTurnIn>(sql, new { id, tenantId });
            return result.FirstOrDefault();
        }

        public async Task ConfirmTurnIn(Guid id, Guid tenantId, Guid managerUserId,
            int managerCountedCents, string? note)
        {
            // Only a still-submitted turn-in can be confirmed (idempotent against
            // double-confirm). COALESCE keeps any worker note when the manager adds none.
            // variance_cents is left for the reconciliation report to snapshot against the
            // computed expected (the ledger doesn't yet carry per-worker cash attribution).
            const string sql = @"
                UPDATE cash_turn_in
                SET manager_user_id = @managerUserId,
                    manager_counted_cents = @managerCountedCents,
                    status = 'confirmed',
                    note = COALESCE(@note, note),
                    confirmed_at = now()
                WHERE id = @id AND tenant_id = @tenantId AND status = 'submitted'";
            await _db.Execute(sql, new { id, tenantId, managerUserId, managerCountedCents, note });
        }

        public async Task<List<CashTurnIn>> ListPendingTurnIns(Guid tenantId, Guid? eventId)
        {
            var eventClause = eventId.HasValue ? "AND event_id = @eventId" : "";
            var sql = $@"
                SELECT {SelectTurnInColumns}
                FROM cash_turn_in
                WHERE tenant_id = @tenantId AND status = 'submitted' {eventClause}
                ORDER BY submitted_at";
            var result = await _db.Query<CashTurnIn>(sql, new { tenantId, eventId });
            return result.ToList();
        }

        public async Task<List<CashTurnIn>> ListTurnInsWithoutEvent(Guid tenantId, DateTime fromUtc, DateTime toUtc)
        {
            var sql = $@"
                SELECT {SelectTurnInColumns}
                FROM cash_turn_in
                WHERE tenant_id = @tenantId AND event_id IS NULL
                  AND submitted_at >= @fromUtc AND submitted_at < @toUtc
                ORDER BY submitted_at DESC";
            return (await _db.Query<CashTurnIn>(sql, new { tenantId, fromUtc, toUtc })).ToList();
        }

        public async Task<List<CashTurnIn>> ListTurnInsByEvent(Guid tenantId, Guid eventId)
        {
            var sql = $@"
                SELECT {SelectTurnInColumns}
                FROM cash_turn_in
                WHERE tenant_id = @tenantId AND event_id = @eventId
                ORDER BY submitted_at DESC";
            var result = await _db.Query<CashTurnIn>(sql, new { tenantId, eventId });
            return result.ToList();
        }
    }
}
