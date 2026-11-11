namespace webapi.Controllers.API.Data.LessonChange
{
    /// <summary>What a rider (or staff) can do with one lesson booking (Script0319).</summary>
    public class LessonChangeInfoResponse
    {
        public Guid TicketId { get; set; }
        public string Status { get; set; } = "";
        public string EventTitle { get; set; } = "";
        public string TierName { get; set; } = "";
        public string? RiderName { get; set; }
        public DateTime SessionStartsAt { get; set; }
        public int AmountCents { get; set; }
        public int CutoffHours { get; set; }
        /// <summary>The venue cancelled this session for weather: rebook or refund, whatever the cutoff.</summary>
        public bool WeatherCancelled { get; set; }
        public bool CanRebook { get; set; }
        /// <summary>Why the booking can't be moved now (null when it can).</summary>
        public string? BlockReason { get; set; }
        public bool CanRefund { get; set; }
        /// <summary>Why a weather refund can't be taken online (e.g. paid in cash), when it can't.</summary>
        public string? RefundNote { get; set; }
        /// <summary>The buyer also rented a bike for this session (staff see this as a warning).</summary>
        public bool HasRental { get; set; }
        public List<LessonRebookOptionItem> Options { get; set; } = new();
    }
}
