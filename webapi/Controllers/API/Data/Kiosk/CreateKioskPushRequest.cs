namespace webapi.Controllers.API.Data.Kiosk
{
    /// <summary>Send a waiver and / or photo step for one rider to one kiosk.</summary>
    public class CreateKioskPushRequest
    {
        public Guid KioskDeviceId { get; set; }
        /// <summary>ticket | season_pass | kiosk_signup</summary>
        public string TargetKind { get; set; } = string.Empty;
        /// <summary>The ticket or season pass purchase (ticket / season_pass targets).</summary>
        public Guid? PurchaseId { get; set; }
        /// <summary>For a pass: the event it is being judged for, as at the gate.</summary>
        public Guid? EventId { get; set; }
        /// <summary>A ticket target only: the ticket the gate checks the photo on, when it is not PurchaseId (a
        /// registrant's rider ticket, while the waiver is fixed on their first ticket). Same event and buyer.</summary>
        public Guid? PhotoPurchaseId { get; set; }
        public Guid? KioskSignupId { get; set; }
        public bool Waiver { get; set; }
        public bool Photo { get; set; }
    }
}
