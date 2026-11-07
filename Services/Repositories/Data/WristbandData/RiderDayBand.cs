namespace Services.Repositories.Data.WristbandData
{
    /// <summary>
    /// One physical wristband on one rider for one tenant business date (Script0312). The rider is keyed
    /// the way the gate keys photos and ID checks (RiderPhotoRules: OwnerKey + RiderKey).
    /// </summary>
    public class RiderDayBand
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public DateOnly BusinessDate { get; set; }
        public string Code { get; set; } = string.Empty;
        public string OwnerKey { get; set; } = string.Empty;
        public string RiderKey { get; set; } = string.Empty;
        public Guid? RiderUserId { get; set; }
        public string RiderName { get; set; } = string.Empty;
        public Guid? SeasonPassPurchaseId { get; set; }
        /// <summary>The ticket the band was issued through (Script0313); null for a pass-issued band.</summary>
        public Guid? EventTicketPurchaseId { get; set; }
        public Guid? IssuedByUserId { get; set; }
        public string? IssuedByName { get; set; }
        public DateTime IssuedAt { get; set; }
        public DateTime? VoidedAt { get; set; }
        public Guid? VoidedByUserId { get; set; }
        public string? VoidReason { get; set; }
        public Guid? ReplacedByBandId { get; set; }
    }
}
