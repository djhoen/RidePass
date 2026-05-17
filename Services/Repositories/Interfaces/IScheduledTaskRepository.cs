using Services.Repositories.Data.ScheduledData;

namespace Services.Repositories.Interfaces
{
    public interface IScheduledTaskRepository
    {
        /// <summary>Enqueue a new task. Returns the new row's id.</summary>
        Task<Guid> Enqueue(Guid tenantId, string kind, string payloadJson, DateTime runAtUtc,
            Guid? createdByUserId, int maxAttempts = 3);

        /// <summary>
        /// Atomically claim up to <paramref name="batchSize"/> due tasks
        /// (status='pending' AND run_at_utc &lt;= now()) and flip them to
        /// 'running'. Uses FOR UPDATE SKIP LOCKED so concurrent dispatchers
        /// claim disjoint sets without blocking each other.
        /// </summary>
        Task<List<ScheduledTask>> ClaimDue(int batchSize);

        /// <summary>Mark a claimed task as succeeded with an optional summary string.</summary>
        Task MarkSucceeded(Guid id, string? resultSummary);

        /// <summary>
        /// Mark a claimed task as failed. If <paramref name="exhausted"/> is true,
        /// status moves to 'failed' (terminal). Otherwise the row goes back to
        /// 'pending' with run_at_utc pushed out by a backoff so the dispatcher
        /// retries on a later tick.
        /// </summary>
        Task MarkFailed(Guid id, string errorMessage, bool exhausted, DateTime? nextRunAtUtc);

        /// <summary>List pending tasks for a tenant, optionally filtered by event id stored in payload.</summary>
        Task<List<ScheduledTask>> ListPendingForTenant(Guid tenantId, Guid? eventIdFilter);

        /// <summary>Mark a pending task as cancelled. Idempotent — no-op if already terminal.</summary>
        Task Cancel(Guid id, Guid tenantId, Guid cancelledByUserId);

        /// <summary>Read a single task (for admin detail / debugging).</summary>
        Task<ScheduledTask?> GetById(Guid id, Guid tenantId);
    }
}
