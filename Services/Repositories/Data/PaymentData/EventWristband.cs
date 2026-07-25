namespace Services.Repositories.Data.PaymentData
{
    /// <summary>A serialized wristband linked to one admission at the gate. The code is the band's
    /// QR payload or printed number, meaningless until linked and unique per scope after. The
    /// admission is EITHER an event ticket or a season pass admission, never both: see
    /// chk_event_wristband_anchor. Scope is the event when one ran, otherwise ValidOnDate.</summary>
    public class EventWristband
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        /// <summary>NULL when the band was issued on a day with no calendar event.</summary>
        public Guid? EventId { get; set; }
        /// <summary>Set when this band belongs to an event ticket; NULL for a season pass admission.</summary>
        public Guid? TicketId { get; set; }
        /// <summary>Set when this band belongs to a season pass admission; NULL for a ticket.</summary>
        public Guid? SeasonPassReservationId { get; set; }
        /// <summary>Tenant-local date this band is good for. Only set when EventId is NULL, where it
        /// is the scope unit the code must be unique within.</summary>
        public DateOnly? ValidOnDate { get; set; }
        public string Code { get; set; } = null!;
        public Guid? LinkedByUserId { get; set; }
        public DateTime LinkedAt { get; set; }
    }

    /// <summary>A band resolved back to its wearer, with everything gate staff need on screen.
    /// Covers both anchors; Source says which one, and the fields that do not apply are null.</summary>
    public class WristbandResolution
    {
        /// <summary>"ticket" or "season_pass": which anchor this band resolved through.</summary>
        public string Source { get; set; } = null!;
        public Guid? TicketId { get; set; }
        public Guid? EventId { get; set; }
        /// <summary>The season pass admission, when Source is "season_pass".</summary>
        public Guid? ReservationId { get; set; }
        /// <summary>The pass purchase behind that admission, when Source is "season_pass".</summary>
        public Guid? PassPurchaseId { get; set; }
        public string Code { get; set; } = null!;
        public Guid RedemptionToken { get; set; }
        public string EventTitle { get; set; } = null!;
        public string? TierName { get; set; }
        /// <summary>Status of whatever this band resolved to: the ticket's, or the pass's.</summary>
        public string Status { get; set; } = null!;
        public string? RiderFirstName { get; set; }
        public string? RiderLastName { get; set; }
        public string PurchaserName { get; set; } = null!;
        public string? RaceNumber { get; set; }
        public DateTime LinkedAt { get; set; }
    }
}
