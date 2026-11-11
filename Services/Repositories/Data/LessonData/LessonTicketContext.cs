namespace Services.Repositories.Data.LessonData
{
    /// <summary>A ticket with what the lesson date-change rules need (Script0319).</summary>
    public class LessonTicketContext
    {
        public Guid TicketId { get; set; }
        public string Status { get; set; } = "";
        public Guid TierId { get; set; }
        public string TierName { get; set; } = "";
        public string TierKind { get; set; } = "";
        public string? TierAudience { get; set; }
        public Guid EventId { get; set; }
        public string EventTitle { get; set; } = "";
        public Guid EventTypeId { get; set; }
        public string? EventTypeCode { get; set; }
        public string EventStatus { get; set; } = "";
        /// <summary>The group's own start when it has one, else the event's.</summary>
        public DateTime SessionStartsAt { get; set; }
        public DateTime? WeatherCancelledAt { get; set; }
        public Guid? WeatherCancelledByUserId { get; set; }
        public Guid? PurchaserUserId { get; set; }
        public string PurchaserEmail { get; set; } = "";
        public string PurchaserName { get; set; } = "";
        public string? RiderName { get; set; }
        public Guid RedemptionToken { get; set; }
        public int AmountCents { get; set; }
        public string PaymentMethod { get; set; } = "";
        public bool IsLesson => string.Equals(EventTypeCode, Services.Lessons.LessonChangeRules.LessonTypeCode, StringComparison.OrdinalIgnoreCase);
    }
}
