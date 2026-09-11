using System.Text.Json;
using System.Text.Json.Serialization;

namespace Services.Repositories.Data.NewsletterData
{
    /// <summary>
    /// A saved, rule-based audience. The definition is evaluated live every time it is used, so
    /// "Season pass holders" includes whoever holds a pass at send time, not whoever did when the
    /// audience was written. Campaigns send to one; automations can start when someone joins one.
    /// </summary>
    public class Audience
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        /// <summary>Raw jsonb; parsed by <see cref="AudienceDefinition.Parse"/>.</summary>
        public string Definition { get; set; } = "{}";
        public bool IsSample { get; set; }
        public Guid? CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>Who the audience starts from before any rule is applied.</summary>
    public static class AudienceBases
    {
        /// <summary>Everyone the track has an email for: subscribers, buyers, and unfinished checkouts.</summary>
        public const string Everyone = "everyone";
        public const string Subscribers = "subscribers";
        /// <summary>Anyone who has paid for a ticket or a pass.</summary>
        public const string Customers = "customers";

        public static readonly string[] All = { Everyone, Subscribers, Customers };
        public static bool IsValid(string? b) => b is not null && Array.IndexOf(All, b) >= 0;
    }

    public static class AudienceRuleKinds
    {
        public const string EventPurchased = "event_purchased";           // Ids = events (empty = any event), optional start window
        public const string EventTypePurchased = "event_type_purchased";  // Ids = event types, optional start window
        public const string PassHolder = "pass_holder";                   // Ids = pass products (empty = any), ActiveOnly
        public const string PassExpiring = "pass_expiring";               // Days: pass ends within N days
        public const string AbandonedCart = "abandoned_cart";             // Days: started checkout, never paid, in the last N days (not today)
        public const string PostalCode = "postal_code";                   // Values: full or partial ZIPs
        public const string State = "state";                              // Values: state codes
        public const string City = "city";                                // Values: city names

        public static readonly string[] All =
            { EventPurchased, EventTypePurchased, PassHolder, PassExpiring, AbandonedCart, PostalCode, State, City };
        public static bool IsValid(string? k) => k is not null && Array.IndexOf(All, k) >= 0;
    }

    public class AudienceRule
    {
        public string Kind { get; set; } = AudienceRuleKinds.EventPurchased;
        /// <summary>"is not": the rule excludes instead of includes.</summary>
        public bool Negate { get; set; }
        public List<Guid> Ids { get; set; } = new();
        public List<string> Values { get; set; } = new();
        public int? Days { get; set; }
        public bool ActiveOnly { get; set; }
        public DateTime? FromUtc { get; set; }
        public DateTime? ToUtc { get; set; }
    }

    public class AudienceDefinition
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public string Base { get; set; } = AudienceBases.Everyone;
        /// <summary>"all" = every rule must hold (AND); "any" = one is enough (OR).</summary>
        public string Match { get; set; } = "all";
        public List<AudienceRule> Rules { get; set; } = new();

        public static AudienceDefinition Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new AudienceDefinition();
            try { return JsonSerializer.Deserialize<AudienceDefinition>(json, Json) ?? new AudienceDefinition(); }
            catch (JsonException) { return new AudienceDefinition(); }
        }

        public string ToJson() => JsonSerializer.Serialize(this, Json);
    }

    /// <summary>One person resolved from an audience. Email is lower-cased.</summary>
    public record AudienceRecipient(string Email, string? Name, Guid? UserId, string? Phone);

    /// <summary>How many things point at an audience, so deleting one can refuse plainly.</summary>
    public class AudienceUsage
    {
        public Guid AudienceId { get; set; }
        public int Campaigns { get; set; }
        public int Automations { get; set; }
    }
}
