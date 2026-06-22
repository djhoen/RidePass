namespace webapi.Controllers.API.Data.Cash
{
    public class OpenCashSessionRequest
    {
        // Null = a general (non-event) counter session. A worker has at most one open
        // session per event at a time; re-opening returns the existing one.
        public Guid? EventId { get; set; }
        public int OpeningFloatCents { get; set; }
        public string? DeviceId { get; set; }
    }
}
