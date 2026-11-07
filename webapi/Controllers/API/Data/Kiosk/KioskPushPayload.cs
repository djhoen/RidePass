namespace webapi.Controllers.API.Data.Kiosk
{
    /// <summary>
    /// What a kiosk needs to show a pushed step, and nothing more: the rider's first name, the steps, how the
    /// waiver is signed and which one. Never an email, phone, birthdate or last name.
    /// </summary>
    public class KioskPushPayload
    {
        public Guid PushId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public bool WantWaiver { get; set; }
        public bool WantPhoto { get; set; }
        /// <summary>guardian | adult</summary>
        public string? SignAs { get; set; }
        /// <summary>The document to sign; its text is fetched once (GET Kiosk/Push/{id}/Waiver), not every poll.</summary>
        public Guid? WaiverId { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
    }
}
