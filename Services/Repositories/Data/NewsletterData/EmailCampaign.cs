namespace Services.Repositories.Data.NewsletterData
{
    public class EmailCampaign
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Subject { get; set; } = null!;
        public string BodyHtml { get; set; } = null!;
        public string? BodyText { get; set; }
        public string Status { get; set; } = "draft";
        public DateTime? ScheduledFor { get; set; }
        public DateTime? SentAt { get; set; }
        public int RecipientCount { get; set; }
        public Guid? CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class EmailCampaignSend
    {
        public Guid Id { get; set; }
        public Guid CampaignId { get; set; }
        public Guid? SubscriberId { get; set; }
        public string Email { get; set; } = null!;
        public string? Name { get; set; }
        public DateTime? SentAt { get; set; }
        public string Status { get; set; } = "pending";
        public string? Error { get; set; }
    }
}
