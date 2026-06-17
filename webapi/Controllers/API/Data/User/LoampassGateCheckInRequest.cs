namespace webapi.Controllers.API.Data.User
{
    public class LoampassGateCheckInRequest
    {
        // The scanned Loam Pass QR value (full "{issuer}/QR/{passId}" URL or a bare pass id).
        public string PassQr { get; set; } = null!;
        public Guid EventId { get; set; }
    }
}
