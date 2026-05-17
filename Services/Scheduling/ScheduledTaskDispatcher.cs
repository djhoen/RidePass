using Microsoft.Extensions.Logging;
using Services.Repositories.Interfaces;

namespace Services.Scheduling
{
    /// <summary>
    /// Polls the scheduled_task table for due rows, claims them atomically,
    /// dispatches each to its kind-specific handler, and updates the row's
    /// terminal state. Driven by the TaskRunner's polling loop in prod; the
    /// webapi can also create one on-demand for tests or one-off catch-ups.
    /// </summary>
    public class ScheduledTaskDispatcher
    {
        private readonly IScheduledTaskRepository _repo;
        private readonly IReadOnlyDictionary<string, IScheduledTaskHandler> _handlers;
        private readonly ILogger<ScheduledTaskDispatcher> _logger;

        public ScheduledTaskDispatcher(
            IScheduledTaskRepository repo,
            IEnumerable<IScheduledTaskHandler> handlers,
            ILogger<ScheduledTaskDispatcher> logger)
        {
            _repo = repo;
            // Dedupes if the DI container ever registers two handlers with the same Kind —
            // last-registration wins, with a warning so the misconfig is visible.
            var byKind = new Dictionary<string, IScheduledTaskHandler>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in handlers)
            {
                if (byKind.ContainsKey(h.Kind))
                {
                    logger.LogWarning("Duplicate scheduled-task handler for kind '{Kind}'; last one wins.", h.Kind);
                }
                byKind[h.Kind] = h;
            }
            _handlers = byKind;
            _logger = logger;
        }

        /// <summary>
        /// Process one batch of due tasks. Returns the number actually executed
        /// (zero when the queue is empty). Caller decides cadence; 60s is fine
        /// for non-time-critical work, lower if you need tighter latency.
        /// </summary>
        public async Task<int> RunOnce(int batchSize = 25, CancellationToken ct = default)
        {
            var claimed = await _repo.ClaimDue(batchSize);
            if (claimed.Count == 0) return 0;

            foreach (var task in claimed)
            {
                ct.ThrowIfCancellationRequested();

                if (!_handlers.TryGetValue(task.Kind, out var handler))
                {
                    _logger.LogError("No handler registered for scheduled-task kind '{Kind}' (task {Id})",
                        task.Kind, task.Id);
                    await _repo.MarkFailed(task.Id, $"No handler registered for kind '{task.Kind}'",
                        exhausted: true, nextRunAtUtc: null);
                    continue;
                }

                try
                {
                    var outcome = await handler.Execute(task, ct);
                    if (outcome.Success)
                    {
                        await _repo.MarkSucceeded(task.Id, outcome.ResultSummary);
                    }
                    else
                    {
                        var exhausted = task.Attempts >= task.MaxAttempts;
                        var nextAt = exhausted ? (DateTime?)null : DateTime.UtcNow + BackoffFor(task.Attempts);
                        await _repo.MarkFailed(task.Id, outcome.ErrorMessage ?? "Unknown failure", exhausted, nextAt);
                        _logger.LogWarning("Scheduled task {Id} ({Kind}) failed on attempt {Attempt}: {Error}",
                            task.Id, task.Kind, task.Attempts, outcome.ErrorMessage);
                    }
                }
                catch (Exception ex)
                {
                    var exhausted = task.Attempts >= task.MaxAttempts;
                    var nextAt = exhausted ? (DateTime?)null : DateTime.UtcNow + BackoffFor(task.Attempts);
                    await _repo.MarkFailed(task.Id, ex.Message, exhausted, nextAt);
                    _logger.LogError(ex, "Scheduled task {Id} ({Kind}) threw on attempt {Attempt}",
                        task.Id, task.Kind, task.Attempts);
                }
            }
            return claimed.Count;
        }

        // Exponential-ish backoff: 1m, 5m, 30m. Caps below the typical
        // dispatcher poll interval so retries always get a fresh chance.
        private static TimeSpan BackoffFor(int attemptsSoFar) => attemptsSoFar switch
        {
            1 => TimeSpan.FromMinutes(1),
            2 => TimeSpan.FromMinutes(5),
            _ => TimeSpan.FromMinutes(30),
        };
    }
}
