using System.Text.RegularExpressions;

namespace Services.Helpers
{
    /// <summary>
    /// Turns what gate staff type into the LIKE patterns the gate search matches with
    /// <c>lower(haystack) LIKE ALL (@likes)</c>. Shared by the ticket and the season pass search so
    /// "jake reed", "reed jake" and "jak" behave the same way in both.
    ///
    /// Rules:
    /// - One <c>%word%</c> pattern per word (split on whitespace and commas), lowercased because the
    ///   haystack is lowered and Postgres LIKE is case-sensitive. Every word must appear somewhere,
    ///   in any order, and a partial word matches.
    /// - A query that is only phone characters (digits, spaces, dashes, dots, parentheses, a leading
    ///   plus) with at least 4 digits collapses to ONE digits-only pattern, so "(555) 123-4567",
    ///   "555.123.4567" and "1234567" all find a phone stored as "+1 555 123 4567". The haystack
    ///   carries the phone with its non-digits stripped for exactly this reason.
    /// - Capped at 5 words, so a pasted paragraph cannot build a huge conjunction.
    /// - LIKE metacharacters are escaped: a rider named "100%" or an email with an underscore must
    ///   not turn into a wildcard.
    /// </summary>
    public static class GateSearchTerms
    {
        public const int MaxWords = 5;
        public const int MinPhoneDigits = 4;

        private static readonly Regex PhoneShaped = new(@"^\+?[\d\s\-\.\(\)]+$", RegexOptions.Compiled);

        public static string[] BuildLikePatterns(string? query)
        {
            var trimmed = (query ?? string.Empty).Trim();
            if (trimmed.Length == 0) return Array.Empty<string>();

            var digits = PhoneDigits(trimmed);
            if (digits is not null) return new[] { $"%{digits}%" };

            var words = trimmed
                .Split(new[] { ' ', '\t', ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Take(MaxWords)
                .Select(w => $"%{EscapeLike(w.ToLowerInvariant())}%")
                .ToArray();
            return words.Length > 0
                ? words
                : new[] { $"%{EscapeLike(trimmed.ToLowerInvariant())}%" };
        }

        /// <summary>The digits of a phone-shaped query, or null when the query is not one.</summary>
        public static string? PhoneDigits(string? query)
        {
            var trimmed = (query ?? string.Empty).Trim();
            if (!PhoneShaped.IsMatch(trimmed)) return null;
            var digits = new string(trimmed.Where(char.IsAsciiDigit).ToArray());
            return digits.Length >= MinPhoneDigits ? digits : null;
        }

        public static string EscapeLike(string value) =>
            value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
    }
}
