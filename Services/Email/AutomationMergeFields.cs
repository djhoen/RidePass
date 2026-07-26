using System.Net;
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
        /// <summary>What the editor lists, in the order it lists them.</summary>
        public static readonly (string Token, string Description)[] Available =
        {
            ("first_name",        "The rider's first name (\"there\" if unknown)"),
            ("holder_name",       "The rider's full name"),
            ("pass_name",         "The pass they hold, e.g. \"Season Pass\""),
            ("expires_on",        "The date their pass runs out"),
            ("credits_remaining", "Rides left, for a credit pack (empty for unlimited passes)"),
            ("upgrade_name",      "The pass they can move up to"),
            ("upgrade_price",     "What the upgrade costs, e.g. \"$125.00\""),
            ("upgrade_link",      "A link straight to their upgrade page"),
            ("track_name",        "Your track's name"),
        };

        /// <summary>
        /// Build the token values for one pass. <paramref name="baseUrl"/> is the tenant's site
        /// root, e.g. https://motoland.ridepass.io.
        /// </summary>
        public static Dictionary<string, string> For(AutomationPassSubject s, string trackName, string baseUrl)
        {
            var first = (s.HolderName ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["first_name"] = string.IsNullOrWhiteSpace(first) ? "there" : first,
                ["holder_name"] = s.HolderName ?? "",
                ["pass_name"] = s.ProductName,
                ["expires_on"] = s.ValidToDate.ToString("MMMM d, yyyy"),
                // Empty, not "0" and not "unlimited": an unlimited pass has no credit count, and
                // rendering a number there would be a lie in an email the rider acts on.
                ["credits_remaining"] = s.CreditsRemaining?.ToString() ?? "",
                ["upgrade_name"] = s.UpgradeProductName ?? "",
                // Empty rather than "$0.00" when no upgrade is configured; "$0.00" reads as free.
                ["upgrade_price"] = s.UpgradePriceCents is int c ? $"${c / 100m:0.00}" : "",
                ["upgrade_link"] = $"{baseUrl.TrimEnd('/')}/User/PassUpgrade/{s.PurchaseId}",
                ["track_name"] = trackName,
            };
        }

        /// <summary>Placeholder values for a test send when the tenant has no eligible pass yet.</summary>
        public static Dictionary<string, string> Sample(string trackName, string baseUrl) =>
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["first_name"] = "Alex",
                ["holder_name"] = "Alex Rivera",
                ["pass_name"] = "Season Pass",
                ["expires_on"] = DateTime.UtcNow.AddMonths(6).ToString("MMMM d, yyyy"),
                ["credits_remaining"] = "3",
                ["upgrade_name"] = "Season Pass Plus",
                ["upgrade_price"] = "$125.00",
                ["upgrade_link"] = $"{baseUrl.TrimEnd('/')}/User/MyPasses",
                ["track_name"] = trackName,
            };

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
