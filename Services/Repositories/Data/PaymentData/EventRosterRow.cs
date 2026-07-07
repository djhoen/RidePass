namespace Services.Repositories.Data.PaymentData
{
    // One attendee row for an event's check-in roster: the cheap, filterable attributes the
    // operator app needs (race class / gate-fee tier, rider vs spectator, checked-in state,
    // race number). Powers the live roster view and the offline roster snapshot. "Checked
    // in" = Status == "redeemed". Per-rider waiver status is intentionally left out here (it
    // needs a heavier join) and surfaced on the per-attendee lookup instead.
    public class EventRosterRow
    {
        public Guid PurchaseId { get; set; }
        public Guid RedemptionToken { get; set; }
        public Guid? PurchaserUserId { get; set; }
        public string? PurchaserName { get; set; }
        public string? PurchaserEmail { get; set; }
        public string? RaceNumber { get; set; }
        public string Status { get; set; } = null!;        // paid | redeemed
        public bool RegistrationComplete { get; set; }
        // Whether this ticket's rider has a waiver signature on file, so the offline operator app
        // can pre-gate locally. The server re-checks authoritatively at AdmitBatch sync.
        public bool WaiverSigned { get; set; }
        public DateTime? RedeemedAtUtc { get; set; }
        public Guid? RedeemedByUserId { get; set; }
        public string TierName { get; set; } = null!;
        public string TierKind { get; set; } = null!;      // race_entry | gate_fee
        public string TierAudience { get; set; } = null!;  // rider | spectator
    }
}
