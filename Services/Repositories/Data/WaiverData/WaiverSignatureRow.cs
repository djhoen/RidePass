namespace Services.Repositories.Data.WaiverData
{
    /// <summary>One row in the admin Signed Waivers log.</summary>
    public class WaiverSignatureRow
    {
        public Guid Id { get; set; }
        public DateTime SignedAt { get; set; }
        public Guid? UserId { get; set; }
        public string? SignerName { get; set; }
        public string? SignerEmail { get; set; }
        public DateTime? Birthdate { get; set; }
        public bool SignedByParent { get; set; }
        public string? ParentName { get; set; }
        public string? ParentPhone { get; set; }
        public string WaiverName { get; set; } = string.Empty;
        public int WaiverVersion { get; set; }
        public bool WaiverIsCurrent { get; set; }
        public bool FromTicket { get; set; }
        public bool FromRental { get; set; }
    }
}
