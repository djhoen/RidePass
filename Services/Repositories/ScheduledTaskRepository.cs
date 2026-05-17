using Services.Helpers.Interfaces;
using Services.Repositories.Data.ScheduledData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class ScheduledTaskRepository : IScheduledTaskRepository
    {
        private const string Columns = @"
            id, tenant_id AS TenantId, kind, payload::text AS Payload, status,
            run_at_utc AS RunAtUtc, attempts, max_attempts AS MaxAttempts,
            last_error AS LastError, result_summary AS ResultSummary,
            started_at_utc AS StartedAtUtc, completed_at_utc AS CompletedAtUtc,
            created_at AS CreatedAt, created_by_user_id AS CreatedByUserId,
            cancelled_at_utc AS CancelledAtUtc, cancelled_by_user_id AS CancelledByUserId";

        private readonly IDbHelper _db;
        public ScheduledTaskRepository(IDbHelper db) => _db = db;

        public async Task<Guid> Enqueue(Guid tenantId, string kind, string payloadJson, DateTime runAtUtc,
            Guid? createdByUserId, int maxAttempts = 3)
        {
            const string sql = @"
                INSERT INTO scheduled_task (tenant_id, kind, payload, run_at_utc, max_attempts, created_by_user_id)
                VALUES (@tenantId, @kind, @payloadJson::jsonb, @runAtUtc, @maxAttempts, @createdByUserId)
                RETURNING id";
            return (await _db.Query<Guid>(sql, new { tenantId, kind, payloadJson, runAtUtc, maxAttempts, createdByUserId })).First();
        }

        public async Task<List<ScheduledTask>> ClaimDue(int batchSize)
        {
            // SKIP LOCKED + transaction-per-statement means two dispatcher
            // processes never claim the same row. Each claim bumps attempts so
            // the handler sees how many tries this is.
            var sql = $@"
                UPDATE scheduled_task
                SET status = 'running',
                    started_at_utc = now(),
                    attempts = attempts + 1
                WHERE id IN (
                    SELECT id FROM scheduled_task
                    WHERE status = 'pending' AND run_at_utc <= now()
                    ORDER BY run_at_utc
                    LIMIT @batchSize
                    FOR UPDATE SKIP LOCKED
                )
                RETURNING {Columns}";
            return (await _db.Query<ScheduledTask>(sql, new { batchSize })).ToList();
        }

        public async Task MarkSucceeded(Guid id, string? resultSummary)
        {
            const string sql = @"
                UPDATE scheduled_task
                SET status = 'succeeded',
                    completed_at_utc = now(),
                    result_summary = @resultSummary,
                    last_error = NULL
                WHERE id = @id AND status = 'running'";
            await _db.Execute(sql, new { id, resultSummary });
        }

        public async Task MarkFailed(Guid id, string errorMessage, bool exhausted, DateTime? nextRunAtUtc)
        {
            // exhausted = true → terminal failure. otherwise reset to pending
            // and bump run_at_utc out by the backoff so the next poll waits.
            if (exhausted)
            {
                const string sql = @"
                    UPDATE scheduled_task
                    SET status = 'failed',
                        completed_at_utc = now(),
                        last_error = @errorMessage
                    WHERE id = @id AND status = 'running'";
                await _db.Execute(sql, new { id, errorMessage });
            }
            else
            {
                const string sql = @"
                    UPDATE scheduled_task
                    SET status = 'pending',
                        run_at_utc = @nextRunAtUtc,
                        started_at_utc = NULL,
                        last_error = @errorMessage
                    WHERE id = @id AND status = 'running'";
                await _db.Execute(sql, new { id, errorMessage, nextRunAtUtc = nextRunAtUtc ?? DateTime.UtcNow });
            }
        }

        public async Task<List<ScheduledTask>> ListPendingForTenant(Guid tenantId, Guid? eventIdFilter)
        {
            // eventIdFilter is null for "all pending in tenant"; when set we
            // filter against payload->>'eventId' so the rider-report's
            // "Scheduled for this event" panel doesn't surface unrelated jobs.
            var where = "tenant_id = @tenantId AND status = 'pending'";
            if (eventIdFilter.HasValue) where += " AND payload->>'eventId' = @eventIdFilter::text";
            var sql = $@"
                SELECT {Columns} FROM scheduled_task
                WHERE {where}
                ORDER BY run_at_utc";
            return (await _db.Query<ScheduledTask>(sql,
                new { tenantId, eventIdFilter = eventIdFilter?.ToString() })).ToList();
        }

        public async Task Cancel(Guid id, Guid tenantId, Guid cancelledByUserId)
        {
            // Only pending tasks can be cancelled. running/succeeded/failed/cancelled stay.
            const string sql = @"
                UPDATE scheduled_task
                SET status = 'cancelled',
                    cancelled_at_utc = now(),
                    cancelled_by_user_id = @cancelledByUserId
                WHERE id = @id AND tenant_id = @tenantId AND status = 'pending'";
            await _db.Execute(sql, new { id, tenantId, cancelledByUserId });
        }

        public async Task<ScheduledTask?> GetById(Guid id, Guid tenantId)
        {
            var sql = $"SELECT {Columns} FROM scheduled_task WHERE id = @id AND tenant_id = @tenantId LIMIT 1";
            return (await _db.Query<ScheduledTask>(sql, new { id, tenantId })).FirstOrDefault();
        }
    }
}
