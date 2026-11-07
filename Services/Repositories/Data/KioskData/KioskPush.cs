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

        /// <summary>A claim (status completing) older than this no longer holds the kiosk: it can be taken again or expire.</summary>
        public static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(2);

        // Why a request ended without being done (end_reason).
        public const string EndDeclined = "declined";
        public const string EndWaiverChanged = "waiver_changed";
        public const string EndModeChanged = "mode_changed";

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
        /// <summary>The ticket the waiver is fixed on (and the photo, unless PhotoTicketPurchaseId is set).</summary>
        public Guid? EventTicketPurchaseId { get; set; }
        /// <summary>The ticket the gate looks for the photo on, when it is not EventTicketPurchaseId.</summary>
        public Guid? PhotoTicketPurchaseId { get; set; }
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
        public DateTime? ClaimedAt { get; set; }
        public Guid? ClaimId { get; set; }
        public string? EndReason { get; set; }
        /// <summary>Read with the open request only: the waiver step's document still exists and has not expired.</summary>
        public bool WaiverUsable { get; set; } = true;
    }
}
