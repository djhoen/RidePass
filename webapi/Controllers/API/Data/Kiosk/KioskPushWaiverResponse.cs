namespace webapi.Controllers.API.Data.Kiosk
{
    /// <summary>The waiver a pushed step asks the kiosk to sign, fetched once when it goes on screen.</summary>
    public class KioskPushWaiverResponse
    {
        public Guid WaiverId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
    }
}
