using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Newsletter
{
    public class CampaignListItem
    {
        public Guid Id { get; set; }
        public string Subject { get; set; } = null!;
        public string Status { get; set; } = null!;
        public int RecipientCount { get; set; }
        public DateTime? SentAtUtc { get; set; }
        public DateTime? ScheduledForUtc { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    public class CampaignDetail : CampaignListItem
    {
        public string BodyHtml { get; set; } = null!;
        public string? BodyText { get; set; }
    }

    public class UpsertCampaignRequest
    {
        [Required] public string Subject { get; set; } = null!;
        [Required] public string BodyHtml { get; set; } = null!;
        public string? BodyText { get; set; }
    }

    public class SendCampaignResponse
    {
        public Guid CampaignId { get; set; }
        public int RecipientCount { get; set; }
        public string Status { get; set; } = null!;
        public string? SendNotice { get; set; }
    }
}
