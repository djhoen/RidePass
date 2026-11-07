namespace webapi.Controllers.API.Data.Kiosk
{
    /// <summary>The kiosk's long-lived secret (shown once; the server keeps only its hash).</summary>
    public class KioskPairResponse
    {
        public string Token { get; set; } = string.Empty;
        public string KioskName { get; set; } = string.Empty;
    }
}
