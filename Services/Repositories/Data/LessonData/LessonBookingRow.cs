namespace Services.Repositories.Data.LessonData
{
    /// <summary>One booking on a lesson session.</summary>
    public class LessonBookingRow
    {
        public Guid TicketId { get; set; }
        public string Status { get; set; } = "";
        public string TierName { get; set; } = "";
        public string RiderName { get; set; } = "";
        public string PurchaserName { get; set; } = "";
        public string PurchaserEmail { get; set; } = "";
        public Guid RedemptionToken { get; set; }
        public int AmountCents { get; set; }
    }
}
