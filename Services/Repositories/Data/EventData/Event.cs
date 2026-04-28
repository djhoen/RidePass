namespace Services.Repositories.Data.EventData
{
    public class Event
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid EventTypeId { get; set; }
        public string Title { get; set; } = null!;
        public string? Description { get; set; }
        public DateTime StartsAt { get; set; }
        public DateTime EndsAt { get; set; }
        public bool AllDay { get; set; }
        public int? Capacity { get; set; }
        public string? LocationLabel { get; set; }
        public string Status { get; set; } = "scheduled";
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class EventWithTypeContext : Event
    {
        public string EventTypeName { get; set; } = null!;
        public string EventTypeColor { get; set; } = null!;
    }
}
