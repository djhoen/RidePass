namespace webapi.Controllers.API.Data.Kiosk
{
    /// <summary>A one-time pairing code to type on the tablet (shown once, valid for 15 minutes).</summary>
    public class KioskPairingCodeResponse
    {
        public Guid DeviceId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public DateTime ExpiresAtUtc { get; set; }
    }
}
