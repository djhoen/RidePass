using Services.Repositories.Data.NewsletterData;

namespace Services.Email
{
    /// <summary>
    /// The registry of what an automation can start from and what a step can be timed against.
    /// The sweep, the controller, the estimate, the test send, and the editor all read from here,
    /// so adding a trigger is: a kind constant, its anchors, its merge fields, and the two SQL
    /// sources in the repository. Design: docs/dynamic-campaigns-plan.md §3.1.
    /// </summary>
    public static class AutomationTriggers
    {
        public const string SeasonPassPurchased = "season_pass_purchased";
        public const string EventTicketPurchased = "event_ticket_purchased";
        public const string NewsletterSubscribed = "newsletter_subscribed";
        /// <summary>Someone newly matches a saved audience (audience_member row appears).</summary>
        public const string AudienceJoined = "audience_joined";

        public static readonly string[] Kinds = { SeasonPassPurchased, EventTicketPurchased, NewsletterSubscribed, AudienceJoined };

        public static bool IsKind(string? kind) => kind is not null && Array.IndexOf(Kinds, kind) >= 0;

        /// <summary>What goes in marketing_automation_send.subject_kind for each trigger.</summary>
        public static string SubjectKindFor(string triggerKind) => triggerKind switch
        {
            SeasonPassPurchased => "season_pass_purchase",
            EventTicketPurchased => "event_ticket_purchase",
            NewsletterSubscribed => "newsletter_subscriber",
            AudienceJoined => "audience_member",
            _ => throw new ArgumentOutOfRangeException(nameof(triggerKind), triggerKind, "Unknown trigger"),
        };

        public static string Label(string triggerKind) => triggerKind switch
        {
            SeasonPassPurchased => "A rider buys a season pass",
            EventTicketPurchased => "A rider buys a ticket to an event",
            NewsletterSubscribed => "Someone joins the newsletter",
            AudienceJoined => "Someone joins an audience",
            _ => triggerKind,
        };

        /// <summary>What a step's wait can be measured from.</summary>
        public static class Anchors
        {
            public const string Purchase = "purchase";
            public const string EventStart = "event_start";
            public const string EventEnd = "event_end";
            public const string PassExpiry = "pass_expiry";
            public const string FixedDate = "fixed_date";

            public static readonly string[] All = { Purchase, EventStart, EventEnd, PassExpiry, FixedDate };
        }

        /// <summary>Anchors a trigger supports, in the order the editor offers them.</summary>
        public static IReadOnlyList<string> AnchorsFor(string triggerKind) => triggerKind switch
        {
            SeasonPassPurchased => new[] { Anchors.Purchase, Anchors.PassExpiry, Anchors.FixedDate },
            EventTicketPurchased => new[] { Anchors.Purchase, Anchors.EventStart, Anchors.EventEnd, Anchors.FixedDate },
            NewsletterSubscribed => new[] { Anchors.Purchase, Anchors.FixedDate },
            AudienceJoined => new[] { Anchors.Purchase, Anchors.FixedDate },
            _ => Array.Empty<string>(),
        };

        /// <summary>The phrase that follows "N days before/after". The 'purchase' anchor is the
        /// moment the subject came into being, which for a newsletter signup is the signup.</summary>
        public static string AnchorPhrase(string anchor, string? triggerKind = null) => anchor switch
        {
            Anchors.Purchase => triggerKind == NewsletterSubscribed ? "they subscribe"
                : triggerKind == AudienceJoined ? "they join" : "they buy",
            Anchors.EventStart => "the event starts",
            Anchors.EventEnd => "the event ends",
            Anchors.PassExpiry => "their pass expires",
            Anchors.FixedDate => "a date you choose",
            _ => anchor,
        };

        /// <summary>"7 days before the event starts", "Straight away after they buy", "On May 1, 2026".</summary>
        public static string DescribeStep(string anchor, int offsetDays, DateTime? sendOn, string? triggerKind = null)
        {
            if (anchor == Anchors.FixedDate)
            {
                return sendOn is DateTime d ? $"On {d:MMMM d, yyyy}" : "On a date you choose";
            }
            var phrase = AnchorPhrase(anchor, triggerKind);
            if (offsetDays == 0) return $"Straight away after {phrase}";
            var n = Math.Abs(offsetDays);
            var unit = n == 1 ? "day" : "days";
            return offsetDays < 0 ? $"{n} {unit} before {phrase}" : $"{n} {unit} after {phrase}";
        }

        /// <summary>
        /// Validates one step's timing against its trigger. Returns a message for the tenant, or
        /// null when it is fine.
        /// </summary>
        public static string? ValidateStep(string triggerKind, string anchor, int offsetDays, DateTime? sendOn)
        {
            if (!AnchorsFor(triggerKind).Contains(anchor))
            {
                return $"\"{AnchorPhrase(anchor, triggerKind)}\" is not a timing this trigger supports.";
            }
            if (anchor == Anchors.FixedDate)
            {
                return sendOn is null ? "Pick the date this email should send on." : null;
            }
            if (sendOn is not null) return "Only a fixed-date email carries a date.";
            if (anchor == Anchors.Purchase && offsetDays < 0)
            {
                return triggerKind == NewsletterSubscribed
                    ? "An email can't send before the signup that starts it."
                    : triggerKind == AudienceJoined
                        ? "An email can't send before they join the audience."
                        : "An email can't send before the purchase that starts it.";
            }
            if (offsetDays < -365 || offsetDays > 3650)
            {
                return "Keep the wait within a year before or ten years after.";
            }
            return null;
        }

        /// <summary>The tenant-facing sentence for the list: what starts it and what it targets.</summary>
        public static string DescribeTrigger(string triggerKind, string? targetName)
        {
            return triggerKind switch
            {
                SeasonPassPurchased => targetName is null ? "Buys any pass" : $"Buys {targetName}",
                EventTicketPurchased => targetName is null ? "Buys an event ticket" : $"Buys a ticket to {targetName}",
                NewsletterSubscribed => "Joins the newsletter",
                AudienceJoined => targetName is null ? "Joins an audience" : $"Joins {targetName}",
                _ => triggerKind,
            };
        }
    }

    /// <summary>Trigger-specific configuration stored as jsonb on the automation.</summary>
    public class AutomationTriggerConfig
    {
        private static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };

        /// <summary>Pass trigger: the product, or null for any pass.</summary>
        public Guid? FromProductId { get; set; }
        /// <summary>Event trigger: exactly one of these is set.</summary>
        public Guid? EventId { get; set; }
        public Guid? EventTypeId { get; set; }
        /// <summary>Audience trigger: the saved audience.</summary>
        public Guid? AudienceId { get; set; }

        public static AutomationTriggerConfig Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new AutomationTriggerConfig();
            try { return System.Text.Json.JsonSerializer.Deserialize<AutomationTriggerConfig>(json, Json) ?? new AutomationTriggerConfig(); }
            catch (System.Text.Json.JsonException) { return new AutomationTriggerConfig(); }
        }

        public string ToJson() => System.Text.Json.JsonSerializer.Serialize(this, Json);

        public static AutomationTriggerConfig For(MarketingAutomation a) => Parse(a.TriggerConfig);
    }
}
