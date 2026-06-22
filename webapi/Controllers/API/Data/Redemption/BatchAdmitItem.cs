namespace webapi.Controllers.API.Data.Redemption
{
    // One offline admission the app is syncing back. Keyed by the scanned redemption token.
    public class BatchAdmitItem
    {
        public Guid RedemptionToken { get; set; }
        // When the admit physically happened at the gate (offline). Recorded as the
        // redemption time so the row reflects reality, not when the device reconnected.
        public DateTime AdmittedAtUtc { get; set; }
        // Optional client-side correlation id so the app can match a result to its queue item.
        public string? ClientRef { get; set; }
    }
}
