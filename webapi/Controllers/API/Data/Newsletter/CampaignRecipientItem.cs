namespace webapi.Controllers.API.Data.Newsletter
{
    /// <summary>One person's send and what followed: opened, clicked, bought.</summary>
    public class CampaignRecipientItem
    {
        public Guid Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public string? Name { get; set; }
        /// <summary>'email' or 'sms'.</summary>
        public string Channel { get; set; } = "email";
        /// <summary>pending | sent | skipped | failed</summary>
        public string Status { get; set; } = string.Empty;
        /// <summary>Why it was skipped or failed, when it was.</summary>
        public string? Reason { get; set; }
        public DateTime? SentAtUtc { get; set; }
        public DateTime? OpenedAtUtc { get; set; }
        public DateTime? ClickedAtUtc { get; set; }
        public DateTime? BoughtAtUtc { get; set; }
        public long RevenueCents { get; set; }
    }
}
