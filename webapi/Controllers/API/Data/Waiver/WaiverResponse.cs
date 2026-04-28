namespace webapi.Controllers.API.Data.Waiver
{
    public class WaiverResponse
    {
        public Guid Id { get; set; }
        public int Version { get; set; }
        public string Title { get; set; } = null!;
        public string Body { get; set; } = string.Empty;
    }

    public class WaiverSignatureStatusResponse
    {
        public bool HasSignedCurrent { get; set; }
        public Guid? SignatureId { get; set; }
        public DateTime? SignedAt { get; set; }
        public int CurrentVersion { get; set; }
    }
}
