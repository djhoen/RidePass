namespace webapi.Controllers.API.Data.Waiver
{
    public class WaiverResponse
    {
        public Guid Id { get; set; }
        public int Version { get; set; }
        public string Name { get; set; } = "Waiver";
        public string Title { get; set; } = null!;
        public string Body { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime? ExpiresAtUtc { get; set; }
    }

    public class UpsertWaiverRequest
    {
        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.MaxLength(120)]
        public string Name { get; set; } = null!;

        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.MaxLength(200)]
        public string Title { get; set; } = null!;

        public string Body { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public DateTime? ExpiresAtUtc { get; set; }
    }

    public class WaiverSignatureStatusResponse
    {
        public bool HasSignedCurrent { get; set; }
        public Guid? SignatureId { get; set; }
        public DateTime? SignedAt { get; set; }
        public int CurrentVersion { get; set; }
        public string? SignatureDataUrl { get; set; }
        public bool RiderIsMinor { get; set; }
        public bool SignedByParent { get; set; }
        public string? ParentName { get; set; }
        public string? ParentPhone { get; set; }
        public bool RiderHasEmergencyContact { get; set; }
    }

    public class SignWaiverRequest
    {
        public string? SignatureDataUrl { get; set; }
        public string? ParentName { get; set; }
        public string? ParentPhone { get; set; }
    }
}
