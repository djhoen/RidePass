namespace webapi.Controllers.API.Data.Kiosk
{
    /// <summary>The kiosk's answer to a pushed step.</summary>
    public class KioskPushCompleteRequest
    {
        /// <summary>The waiver it showed: refused if the request's waiver has changed.</summary>
        public Guid? WaiverId { get; set; }
        public bool AgreedToWaiver { get; set; }
        public string? SignatureDataUrl { get; set; }
        /// <summary>Guardian mode: the parent or guardian who signs.</summary>
        public string? GuardianName { get; set; }
        public string? GuardianPhone { get; set; }
        public string? PhotoDataUrl { get; set; }
    }
}
