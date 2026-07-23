namespace webapi.Controllers.API.Data.Waiver
{
    /// <summary>One outbound signature request in the admin list.</summary>
    public class WaiverSignRequestItem
    {
        public Guid Id { get; set; }
        public string RecipientEmail { get; set; } = string.Empty;
        public string? RecipientName { get; set; }
        public string? WaiverName { get; set; }
        public int? WaiverVersion { get; set; }
        public string? EventTitle { get; set; }
        public string Status { get; set; } = "pending";
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? SentAtUtc { get; set; }
        public DateTime? OpenedAtUtc { get; set; }
        public DateTime? SignedAtUtc { get; set; }
        /// <summary>The public signing URL, for copy-link.</summary>
        public string Link { get; set; } = string.Empty;
    }
}
