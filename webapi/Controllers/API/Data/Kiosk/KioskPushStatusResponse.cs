namespace webapi.Controllers.API.Data.Kiosk
{
    /// <summary>A pushed step as staff follow it (Waiting for kiosk / On screen / Done / Expired / Cancelled).</summary>
    public class KioskPushStatusResponse
    {
        public Guid Id { get; set; }
        /// <summary>pending | in_progress | completing | done | cancelled | expired</summary>
        public string Status { get; set; } = string.Empty;
        public string KioskName { get; set; } = string.Empty;
        /// <summary>The kiosk is on its start screen asking for work; false = someone is using it ("Kiosk busy").</summary>
        public bool KioskIdle { get; set; }
        public string RiderFirstName { get; set; } = string.Empty;
        public bool WantWaiver { get; set; }
        public bool WantPhoto { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
        /// <summary>Why it ended without being done: declined (at the kiosk), waiver_changed, mode_changed; null
        /// otherwise (timed out / cancelled by staff).</summary>
        public string? EndReason { get; set; }
    }
}
