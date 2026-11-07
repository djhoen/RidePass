namespace webapi.Controllers.API.Data.Kiosk
{
    /// <summary>A kiosk sign-up as the gate screens list it (staff only).</summary>
    public class KioskSignupItem
    {
        public Guid Id { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public DateOnly? Birthdate { get; set; }
        public int? Age { get; set; }
        public bool IsMinor { get; set; }
        public Guid? RiderUserId { get; set; }
        public string? RiderEmail { get; set; }
        public Guid? DependentId { get; set; }
        public Guid? GuardianUserId { get; set; }
        public string? GuardianName { get; set; }
        public string? GuardianEmail { get; set; }
        public bool WaiverSigned { get; set; }
        public Guid? PhotoId { get; set; }
        public DateTime? PhotoTakenAtUtc { get; set; }
        public string? KioskName { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? HandledAtUtc { get; set; }
        public DateTime? DismissedAtUtc { get; set; }
    }
}
