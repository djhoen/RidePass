using System.Net;
using Services.Helpers;
using Services.Repositories.Data.NewsletterData;

namespace Services.Email
{
    /// <summary>
    /// Substitutes <c>{{token}}</c> placeholders in an automation's subject and body.
    ///
    /// Deliberately not a template engine. A drip email is a paragraph with a name and a price in
    /// it, and every expression language shipped to end users eventually becomes a support burden
    /// (or, if it can reach the model, a data-exposure one). Unknown tokens render EMPTY rather
    /// than being left as literal text, so a typo produces an awkward sentence instead of shipping
    /// "{{frist_name}}" to a paying customer.
    /// </summary>
    public static class AutomationMergeFields
    {
        private static readonly (string Token, string Description)[] Common =
        {
            ("first_name", "The rider's first name (\"there\" if unknown)"),
            ("holder_name", "The rider's full name"),
            ("track_name",  "Your track's name"),
        };

        private static readonly (string Token, string Description)[] Pass =
        {
            ("pass_name",         "The pass they hold, e.g. \"Season Pass\""),
            ("expires_on",        "The date their pass runs out"),
            ("credits_remaining", "Rides left, for a credit pack (empty for unlimited passes)"),
            ("upgrade_name",      "The pass they can move up to"),
            ("upgrade_price",     "What the upgrade costs, e.g. \"$125.00\""),
            ("upgrade_link",      "A link straight to their upgrade page"),
        };

        private static readonly (string Token, string Description)[] Event =
        {
            ("event_name",       "The event they bought a ticket to"),
            ("event_date",       "The day it starts, e.g. \"Saturday, June 14\""),
            ("event_start_time", "The start time in your track's timezone (empty for all-day events)"),
            ("event_end_date",   "The day it ends"),
            ("event_location",   "The location on the event, if one is set"),
            ("ticket_tier",      "The ticket they bought, e.g. \"Rider\""),
            ("event_link",       "A link to the event page"),
        };

        /// <summary>Every token, for the legacy MergeFields endpoint.</summary>
        public static readonly (string Token, string Description)[] Available =
            Common.Concat(Pass).Concat(Event).ToArray();

        /// <summary>The tokens a trigger can fill, in the order the editor lists them.</summary>
        public static (string Token, string Description)[] AvailableFor(string triggerKind) => triggerKind switch
        {
            AutomationTriggers.EventTicketPurchased => Common.Concat(Event).ToArray(),
            _ => Common.Concat(Pass).ToArray(),
        };

        /// <summary>
        /// Build the token values for one subject. <paramref name="baseUrl"/> is the tenant's site
        /// root, e.g. https://motoland.ridepass.io; <paramref name="timezone"/> formats event times.
        /// </summary>
        public static Dictionary<string, string> For(AutomationSubject s, string trackName, string baseUrl, string? timezone)
        {
            var first = (s.HolderName ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            var v = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["first_name"] = string.IsNullOrWhiteSpace(first) ? "there" : first,
                ["holder_name"] = s.HolderName ?? "",
                ["track_name"] = trackName,
            };
            var root = baseUrl.TrimEnd('/');

            if (s.SubjectKind == "event_ticket_purchase")
            {
                var start = s.EventStartsAt.HasValue ? SendWindow.ToLocal(s.EventStartsAt.Value, timezone) : (DateTime?)null;
                var end = s.EventEndsAt.HasValue ? SendWindow.ToLocal(s.EventEndsAt.Value, timezone) : (DateTime?)null;
                v["event_name"] = s.ProductName;
                v["event_date"] = start?.ToString("dddd, MMMM d") ?? "";
                v["event_start_time"] = s.EventAllDay || start is null ? "" : start.Value.ToString("h:mm tt");
                v["event_end_date"] = end?.ToString("dddd, MMMM d") ?? "";
                v["event_location"] = s.EventLocation ?? "";
                v["ticket_tier"] = s.TicketTierName ?? "";
                v["event_link"] = s.EventId is Guid eid ? $"{root}/Events/{eid}" : $"{root}/Events";
                return v;
            }

            v["pass_name"] = s.ProductName;
            v["expires_on"] = s.ValidToDate?.ToString("MMMM d, yyyy") ?? "";
            // Empty, not "0" and not "unlimited": an unlimited pass has no credit count, and
            // rendering a number there would be a lie in an email the rider acts on.
            v["credits_remaining"] = s.CreditsRemaining?.ToString() ?? "";
            v["upgrade_name"] = s.UpgradeProductName ?? "";
            // Empty rather than "$0.00" when no upgrade is configured; "$0.00" reads as free.
            v["upgrade_price"] = s.UpgradePriceCents is int c ? $"${c / 100m:0.00}" : "";
            v["upgrade_link"] = $"{root}/User/PassUpgrade/{s.SubjectId}";
            return v;
        }

        /// <summary>Placeholder values for a test send when the tenant has no eligible subject yet.</summary>
        public static Dictionary<string, string> Sample(string triggerKind, string trackName, string baseUrl)
        {
            var root = baseUrl.TrimEnd('/');
            var v = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["first_name"] = "Alex",
                ["holder_name"] = "Alex Rivera",
                ["track_name"] = trackName,
            };
            if (triggerKind == AutomationTriggers.EventTicketPurchased)
            {
                var start = DateTime.UtcNow.AddDays(14);
                v["event_name"] = "Spring Skills Camp";
                v["event_date"] = start.ToString("dddd, MMMM d");
                v["event_start_time"] = "9:00 AM";
                v["event_end_date"] = start.AddDays(2).ToString("dddd, MMMM d");
                v["event_location"] = "Main lodge";
                v["ticket_tier"] = "Rider";
                v["event_link"] = $"{root}/Events";
                return v;
            }
            v["pass_name"] = "Season Pass";
            v["expires_on"] = DateTime.UtcNow.AddMonths(6).ToString("MMMM d, yyyy");
            v["credits_remaining"] = "3";
            v["upgrade_name"] = "Season Pass Plus";
            v["upgrade_price"] = "$125.00";
            v["upgrade_link"] = $"{root}/User/MyPasses";
            return v;
        }

        /// <summary>
        /// Replace every <c>{{token}}</c>. <paramref name="htmlEncode"/> for bodies that are HTML
        /// (a rider named "Bob &amp; Sue" must not break the markup) and false for plain text.
        /// </summary>
        public static string Render(string template, IReadOnlyDictionary<string, string> values, bool htmlEncode)
        {
            if (string.IsNullOrEmpty(template) || template.IndexOf("{{", StringComparison.Ordinal) < 0)
            {
                return template ?? "";
            }

            var sb = new System.Text.StringBuilder(template.Length);
            var i = 0;
            while (i < template.Length)
            {
                var open = template.IndexOf("{{", i, StringComparison.Ordinal);
                if (open < 0) { sb.Append(template, i, template.Length - i); break; }
                var close = template.IndexOf("}}", open + 2, StringComparison.Ordinal);
                if (close < 0) { sb.Append(template, i, template.Length - i); break; }

                sb.Append(template, i, open - i);
                var name = template.Substring(open + 2, close - open - 2).Trim();
                // Unknown token -> empty. See the class remark: a typo must not reach the rider.
                var value = values.TryGetValue(name, out var v) ? v : "";
                sb.Append(htmlEncode ? WebUtility.HtmlEncode(value) : value);
                i = close + 2;
            }
            return sb.ToString();
        }
    }
}
