namespace Services.Repositories.Data.NewsletterData
{
    /// <summary>One SendGrid open or click, tied to the send row the email came from.</summary>
    public class EmailEngagement
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        /// <summary>'campaign' (email_campaign_send) or 'automation' (marketing_automation_send).</summary>
        public string SourceKind { get; set; } = null!;
        public Guid SourceSendId { get; set; }
        /// <summary>'open' or 'click'.</summary>
        public string Event { get; set; } = null!;
        public string? Url { get; set; }
        /// <summary>SendGrid's sg_event_id; the dedupe key because their webhook retries.</summary>
        public string? SgEventId { get; set; }
        public DateTime OccurredAt { get; set; }
    }

    /// <summary>Rollup per campaign or per automation step: distinct people, not raw events.</summary>
    public class EmailEngagementStats
    {
        public Guid Key { get; set; }
        public int UniqueOpens { get; set; }
        public int UniqueClicks { get; set; }
        public int TotalClicks { get; set; }
    }

    /// <summary>Which links got clicked in a campaign, and by how many distinct people.</summary>
    public class EmailClickUrlStats
    {
        public string Url { get; set; } = null!;
        public int UniqueClickers { get; set; }
        public int TotalClicks { get; set; }
    }

    /// <summary>A saved body a track can start a campaign or an automation email from.</summary>
    public class EmailTemplate
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = null!;
        public string? Subject { get; set; }
        public string? PreviewText { get; set; }
        public string BodyHtml { get; set; } = null!;
        public Guid? CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
