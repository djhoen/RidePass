using Services.Repositories.Data.ScheduledData;

namespace Services.Scheduling
{
    /// <summary>
    /// One handler per `kind` slug on the scheduled_task table. The dispatcher
    /// looks up the handler by Kind, deserialises the payload jsonb itself
    /// (handlers receive the raw JSON string), and lets the handler execute.
    /// </summary>
    public interface IScheduledTaskHandler
    {
        /// <summary>The `kind` value this handler processes. e.g., "send_rider_message".</summary>
        string Kind { get; }

        /// <summary>
        /// Execute the task. Return success=false with a human-readable error to
        /// trigger the dispatcher's retry/backoff logic; throw for unexpected
        /// crashes (the dispatcher catches and treats as failed).
        /// </summary>
        Task<ScheduledTaskOutcome> Execute(ScheduledTask task, CancellationToken ct);
    }

    public record ScheduledTaskOutcome(bool Success, string? ResultSummary, string? ErrorMessage)
    {
        public static ScheduledTaskOutcome Ok(string? summary = null) => new(true, summary, null);
        public static ScheduledTaskOutcome Fail(string message) => new(false, null, message);
    }
}
