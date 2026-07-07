namespace Services.Repositories.Data.ReportData
{
    // One row per event ticket in the "who has signed" report: the attendee, their audience, and the
    // waiver signing status read from the normalized rider_waiver_signature store (via the ticket's
    // waiver_signature_id link) so counter and online sales are reported uniformly.
    public class EventWaiverSignatureRow
    {
        public System.Guid PurchaseId { get; set; }
        public string AttendeeName { get; set; } = null!;      // rider name, else purchaser name
        public string Audience { get; set; } = null!;          // rider | spectator
        public string TierName { get; set; } = null!;
        public string? RaceNumber { get; set; }
        public string Status { get; set; } = null!;            // paid | redeemed | ...
        public bool RegistrationComplete { get; set; }
        public bool WaiverRequired { get; set; }
        public bool WaiverSigned { get; set; }
        public System.DateTime? SignedAtUtc { get; set; }
        public bool SignedByParent { get; set; }
        public string? ParentGuardianName { get; set; }
        public string? SignerName { get; set; }
    }
}
