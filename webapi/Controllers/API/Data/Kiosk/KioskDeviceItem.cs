namespace webapi.Controllers.API.Data.Kiosk
{
    /// <summary>A kiosk as Admin > Kiosks lists it (never its secret or code hash).</summary>
    public class KioskDeviceItem
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool Paired { get; set; }
        public DateTime? PairedAtUtc { get; set; }
        public DateTime? LastSeenAtUtc { get; set; }
        public bool PairingPending { get; set; }
        public DateTime? PairingExpiresAtUtc { get; set; }
        public DateTime? RevokedAtUtc { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }
}
