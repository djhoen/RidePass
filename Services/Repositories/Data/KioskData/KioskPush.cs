namespace Services.Repositories.Data.KioskData
{
    /// <summary>
    /// A waiver and / or photo step staff sent to one kiosk for one rider (Script0311). The target is a ticket,
    /// a season pass (+ the event it is judged for), or a kiosk sign-up's profile.
    /// </summary>
    public class KioskPush
    {
        public const string StatusPending = "pending";
        public const string StatusInProgress = "in_progress";
        public const string StatusCompleting = "completing";
        public const string StatusDone = "done";
        public const string StatusCancelled = "cancelled";
        public const string StatusExpired = "expired";

        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid KioskDeviceId { get; set; }
        public Guid? CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public bool WantWaiver { get; set; }
        public bool WantPhoto { get; set; }
        /// <summary>ticket | season_pass | kiosk_signup</summary>
        public string TargetKind { get; set; } = string.Empty;
        public Guid? EventTicketPurchaseId { get; set; }
        public Guid? SeasonPassPurchaseId { get; set; }
        public Guid? EventId { get; set; }
        public Guid? KioskSignupId { get; set; }
        /// <summary>guardian | adult (null when no waiver step).</summary>
        public string? SignMode { get; set; }
        public Guid? WaiverId { get; set; }
        public string RiderFirstName { get; set; } = string.Empty;
        public string Status { get; set; } = StatusPending;
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime? CancelledAt { get; set; }
        public Guid? CancelledByUserId { get; set; }
        public Guid? ResultSignatureId { get; set; }
        public Guid? ResultPhotoId { get; set; }
    }
}
