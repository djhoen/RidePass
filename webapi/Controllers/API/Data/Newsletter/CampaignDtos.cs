using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Newsletter
{
    public class CampaignListItem
    {
        public Guid Id { get; set; }
        public string Subject { get; set; } = null!;
        public string Status { get; set; } = null!;
        public int RecipientCount { get; set; }
        // Audience: kind + a human label ("Purchasers of Spring Camp") + the raw target so the
        // list can re-count before a send without loading the detail.
        public string AudienceKind { get; set; } = "subscribers";
        public string AudienceLabel { get; set; } = string.Empty;
        public CampaignAudienceConfigDto AudienceConfig { get; set; } = new();
        /// <summary>'email' | 'sms' | 'both'.</summary>
        public string Channel { get; set; } = "email";
        /// <summary>Texts delivered (a subset of RecipientCount).</summary>
        public int TextCount { get; set; }
        // Engagement from SendGrid events: distinct people. Opens are inflated by Apple Mail's
        // privacy proxy, so clicks are the honest number; both are shown.
        public int UniqueOpens { get; set; }
        public int UniqueClicks { get; set; }
        public int TotalClicks { get; set; }
        public DateTime? SentAtUtc { get; set; }
        public DateTime? ScheduledForUtc { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    public class CampaignDetail : CampaignListItem
    {
        public string BodyHtml { get; set; } = null!;
        public string? BodyText { get; set; }
        public string? PreviewText { get; set; }
        public string? SmsBody { get; set; }
        public List<CampaignClickUrlItem> ClickUrls { get; set; } = new();
    }

    public class UpsertCampaignRequest
    {
        [Required] public string Subject { get; set; } = null!;
        /// <summary>Required when the channel includes email.</summary>
        public string? BodyHtml { get; set; }
        public string? BodyText { get; set; }
        /// <summary>Inbox snippet under the subject line. Optional.</summary>
        public string? PreviewText { get; set; }
        /// <summary>'email' (default) | 'sms' | 'both'.</summary>
        public string? Channel { get; set; }
        /// <summary>Required when the channel includes sms. Merge fields allowed.</summary>
        public string? SmsBody { get; set; }
        // Omitted = newsletter subscribers (the original behaviour).
        public string? AudienceKind { get; set; }
        public CampaignAudienceConfigDto? AudienceConfig { get; set; }
    }

    public class SendCampaignResponse
    {
        public Guid CampaignId { get; set; }
        public int RecipientCount { get; set; }
        public string Status { get; set; } = null!;
        public string? SendNotice { get; set; }
    }
}
