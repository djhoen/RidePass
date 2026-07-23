namespace webapi.Controllers.API.Data.Waiver
{
    /// <summary>Full signature detail for the admin drill-in / print view.</summary>
    public class WaiverSignatureDetailResponse
    {
        public Guid Id { get; set; }
        public DateTime SignedAtUtc { get; set; }
        public Guid? UserId { get; set; }
        public string? SignerName { get; set; }
        public string? SignerEmail { get; set; }
        public DateTime? Birthdate { get; set; }
        public bool SignedByParent { get; set; }
        public string? ParentName { get; set; }
        public string? ParentPhone { get; set; }
        public string? IpAddress { get; set; }
        public string? SignatureDataUrl { get; set; }
        public string WaiverName { get; set; } = string.Empty;
        public string WaiverTitle { get; set; } = string.Empty;
        public int WaiverVersion { get; set; }
        public string? EmergencyContactName { get; set; }
        public string? EmergencyContactPhone { get; set; }
        public string? TicketEventTitle { get; set; }
        public string? RentalLabel { get; set; }
    }
}
