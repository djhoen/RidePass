namespace webapi.Controllers.API.Data.Waiver
{
    /// <summary>What the public /SignWaiver/{token} page needs to render.</summary>
    public class PublicWaiverSignRequestResponse
    {
        public string Status { get; set; } = "pending";
        public string? RecipientName { get; set; }
        public string RecipientEmail { get; set; } = string.Empty;
        public Guid WaiverId { get; set; }
        public string WaiverName { get; set; } = string.Empty;
        public string WaiverTitle { get; set; } = string.Empty;
        public string WaiverBody { get; set; } = string.Empty;
        public int WaiverVersion { get; set; }
        public bool AlreadySigned { get; set; }
    }
}
