namespace Services.Repositories.Data.PaymentData
{
    /// <summary>A serialized wristband linked to one event entrant at the gate. The code is the
    /// band's QR payload or printed number — meaningless until linked, unique per event after.</summary>
    public class EventWristband
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid EventId { get; set; }
        public Guid TicketId { get; set; }
        public string Code { get; set; } = null!;
        public Guid? LinkedByUserId { get; set; }
        public DateTime LinkedAt { get; set; }
    }

    /// <summary>A band resolved back to its entrant, with everything gate staff need on screen.</summary>
    public class WristbandResolution
    {
        public Guid TicketId { get; set; }
        public Guid EventId { get; set; }
        public string Code { get; set; } = null!;
        public Guid RedemptionToken { get; set; }
        public string EventTitle { get; set; } = null!;
        public string TierName { get; set; } = null!;
        public string TicketStatus { get; set; } = null!;
        public string? RiderFirstName { get; set; }
        public string? RiderLastName { get; set; }
        public string PurchaserName { get; set; } = null!;
        public string? RaceNumber { get; set; }
        public DateTime LinkedAt { get; set; }
    }
}
