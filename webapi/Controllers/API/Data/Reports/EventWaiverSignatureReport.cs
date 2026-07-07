namespace webapi.Controllers.API.Data.Reports
{
    // "Who has signed" report for one event: attendees + their waiver signing status, sourced from the
    // normalized rider_waiver_signature store so counter and online sales report uniformly.
    public class EventWaiverSignatureReportResponse
    {
        public System.Guid EventId { get; set; }
        public string EventTitle { get; set; } = null!;
        public System.DateTime EventStartsAtUtc { get; set; }
        public int TotalAttendees { get; set; }
        // Counts an attendee "signed" when no waiver is required OR a signature is on file.
        public int TotalSigned { get; set; }
        public System.Collections.Generic.List<EventWaiverSignatureRowDto> Rows { get; set; } = new();
    }

    public class EventWaiverSignatureRowDto
    {
        public System.Guid PurchaseId { get; set; }
        public string AttendeeName { get; set; } = null!;
        public string Audience { get; set; } = null!;          // rider | spectator
        public string TierName { get; set; } = null!;
        public string? RaceNumber { get; set; }
        public string Status { get; set; } = null!;
        public bool RegistrationComplete { get; set; }
        public bool WaiverRequired { get; set; }
        public bool WaiverSigned { get; set; }
        public System.DateTime? SignedAtUtc { get; set; }
        public bool SignedByParent { get; set; }
        public string? ParentGuardianName { get; set; }
        public string? SignerName { get; set; }
    }
}
