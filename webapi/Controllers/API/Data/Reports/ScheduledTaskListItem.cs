namespace webapi.Controllers.API.Data.Reports
{
    public class ScheduledTaskListItem
    {
        public Guid Id { get; set; }
        public string Kind { get; set; } = null!;
        public DateTime RunAtUtc { get; set; }
        public string Status { get; set; } = null!;
        public string? Summary { get; set; }            // short human-readable description (e.g., "SMS to 12 riders")
        public DateTime CreatedAtUtc { get; set; }
        public Guid? CreatedByUserId { get; set; }
    }
}
