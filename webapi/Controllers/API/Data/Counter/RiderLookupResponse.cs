namespace webapi.Controllers.API.Data.Counter
{
    public class RiderLookupResponse
    {
        public Guid Id { get; set; }
        public string Email { get; set; } = null!;
        public string FirstName { get; set; } = null!;
        public string LastName { get; set; } = null!;
        public bool HasSignedCurrentWaiver { get; set; }
        public DateTime? WaiverSignedAtUtc { get; set; }
        public string? WaiverSignatureDataUrl { get; set; }
        public bool IsMinor { get; set; }
        public bool WaiverSignedByParent { get; set; }
        public string? WaiverParentName { get; set; }
        public string? WaiverParentPhone { get; set; }
        public string? EmergencyContactName { get; set; }
        public string? EmergencyContactPhone { get; set; }
    }
}
