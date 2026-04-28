namespace webapi.Controllers.API.Data.Event
{
    public class EventResponse
    {
        public Guid Id { get; set; }
        public Guid EventTypeId { get; set; }
        public string EventTypeCode { get; set; } = null!;
        public string EventTypeName { get; set; } = null!;
        public string EventTypeColor { get; set; } = null!;
        public string Title { get; set; } = null!;
        public string? Description { get; set; }
        public DateTime StartsAtUtc { get; set; }
        public DateTime EndsAtUtc { get; set; }
        public bool AllDay { get; set; }
        public int? Capacity { get; set; }
        public string? LocationLabel { get; set; }
        public string Status { get; set; } = null!;
        public bool HasActiveTiers { get; set; }
        public int? MinTicketPriceCents { get; set; }
        public int? SpotsReserved { get; set; } // null if no capacity
    }
}
