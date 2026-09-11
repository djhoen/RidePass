using System.Text.Json;
using System.Text.Json.Serialization;

namespace Services.Repositories.Data.NewsletterData
{
    /// <summary>Who a broadcast campaign goes to. Stored in email_campaign.audience_kind.</summary>
    public static class CampaignAudienceKinds
    {
        public const string Subscribers = "subscribers";   // the newsletter list (default, original behaviour)
        public const string Event = "event";               // purchasers of one event
        public const string EventType = "event_type";      // purchasers of any event of one type (optional date window)
        public const string PassProduct = "pass_product";  // holders of one season pass product
        public const string Audience = "audience";         // a saved, rule-based audience (the Audiences tab)

        public static readonly string[] All = { Subscribers, Event, EventType, PassProduct, Audience };
        public static bool IsValid(string? kind) => kind is not null && Array.IndexOf(All, kind) >= 0;
    }

    /// <summary>
    /// Target ids for the non-subscriber audiences, serialized to email_campaign.audience_config.
    /// Only the field the kind needs is set; the others stay null.
    /// </summary>
    public class CampaignAudienceConfig
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public Guid? EventId { get; set; }
        public Guid? EventTypeId { get; set; }
        public Guid? PassProductId { get; set; }
        /// <summary>Saved audience id for the 'audience' kind.</summary>
        public Guid? AudienceId { get; set; }
        /// <summary>Optional window on event start for the event-type audience (UTC).</summary>
        public DateTime? FromUtc { get; set; }
        public DateTime? ToUtc { get; set; }

        public static CampaignAudienceConfig Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new CampaignAudienceConfig();
            try { return JsonSerializer.Deserialize<CampaignAudienceConfig>(json, Json) ?? new CampaignAudienceConfig(); }
            catch (JsonException) { return new CampaignAudienceConfig(); }
        }

        public string ToJson() => JsonSerializer.Serialize(this, Json);
    }

    /// <summary>One recipient resolved from an audience. SubscriberId is set only for the newsletter list.</summary>
    public record CampaignAudienceRecipient(string Email, string? Name, Guid? SubscriberId, string? Phone);

    public class CampaignAudienceEventOption
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = null!;
        public DateTime StartsAt { get; set; }
        public string Status { get; set; } = null!;
        public string EventTypeName { get; set; } = null!;
    }

    public class CampaignAudienceNamedOption
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public bool IsActive { get; set; }
    }

    /// <summary>Everything the campaign editor needs to offer as an audience target.</summary>
    public class CampaignAudienceOptions
    {
        public List<CampaignAudienceEventOption> Events { get; set; } = new();
        public List<CampaignAudienceNamedOption> EventTypes { get; set; } = new();
        public List<CampaignAudienceNamedOption> PassProducts { get; set; } = new();
    }
}
