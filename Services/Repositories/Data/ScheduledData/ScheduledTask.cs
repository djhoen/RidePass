namespace Services.Repositories.Data.ScheduledData
{
    /// <summary>
    /// One row of the scheduled_task table. `Payload` is the handler-specific
    /// JSON blob — each kind defines its own shape; the dispatcher just routes
    /// by `Kind` and leaves parsing to the handler.
    /// </summary>
    public class ScheduledTask
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Kind { get; set; } = null!;
        public string Payload { get; set; } = null!;     // jsonb serialized
        public string Status { get; set; } = "pending";  // pending | running | succeeded | failed | cancelled
        public DateTime RunAtUtc { get; set; }
        public int Attempts { get; set; }
        public int MaxAttempts { get; set; } = 3;
        public string? LastError { get; set; }
        public string? ResultSummary { get; set; }
        public DateTime? StartedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
        public DateTime CreatedAt { get; set; }
        public Guid? CreatedByUserId { get; set; }
        public DateTime? CancelledAtUtc { get; set; }
        public Guid? CancelledByUserId { get; set; }
    }
}
