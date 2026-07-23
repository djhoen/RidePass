namespace Services.Repositories.Data.WaiverData
{
    /// <summary>One outbound "please sign this waiver" request.</summary>
    public class WaiverSignRequestRow
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid? WaiverId { get; set; }
        public string Token { get; set; } = string.Empty;
        public string RecipientEmail { get; set; } = string.Empty;
        public string? RecipientName { get; set; }
        public Guid? EventId { get; set; }
        public string Status { get; set; } = "pending";
        public Guid? SignatureId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? SentAt { get; set; }
        public DateTime? OpenedAt { get; set; }
        public DateTime? SignedAt { get; set; }
        public string? WaiverName { get; set; }
        public int? WaiverVersion { get; set; }
        public string? EventTitle { get; set; }
    }
}
