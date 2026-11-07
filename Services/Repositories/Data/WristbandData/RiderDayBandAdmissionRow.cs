namespace Services.Repositories.Data.WristbandData
{
    /// <summary>An admission a day band covers: a season pass reservation (item 9) or a ticket (item 10).</summary>
    public class RiderDayBandAdmissionRow
    {
        public Guid BandId { get; set; }
        public Guid? SeasonPassReservationId { get; set; }
        public Guid? EventTicketPurchaseId { get; set; }
        public Guid? EventId { get; set; }
        public string? EventTitle { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime? CheckedInAt { get; set; }
        public DateTime LinkedAt { get; set; }
    }
}
