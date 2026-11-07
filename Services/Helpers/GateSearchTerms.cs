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
    /// - A run of consecutive phone-shaped words (digits, dashes, dots, parentheses, a leading plus)
    ///   holding at least 4 digits becomes ONE digits-only pattern, so "(555) 123-4567",
    ///   "555.123.4567" and "smith 555 123 4567" all reach a phone stored as "+1 555 123 4567". The
    ///   haystack carries the phone with its non-digits stripped for exactly this reason. An 11-digit
    ///   number starting with the North American country code 1 drops the 1, so "+1 555 123 4567"
    ///   also finds a phone stored without it.
    /// - Capped at 5 patterns, so a pasted paragraph cannot build a huge conjunction.
    /// - LIKE metacharacters are escaped: a rider named "100%" or an email with an underscore must
    ///   not turn into a wildcard.
    /// </summary>
    public static class GateSearchTerms
    {
        public const int MaxWords = 5;
        public const int MinPhoneDigits = 4;

        /// <summary>
        /// The linked account's part of the searchable text (alias <c>u</c> = users): email, name and
        /// phone digits. Shared by both gate searches so they match accounts the same way.
        /// </summary>
        public const string AccountHaystackSql = @"
                        COALESCE(u.email, '') || ' ' ||
                        COALESCE(u.first_name, '') || ' ' ||
                        COALESCE(u.last_name, '') || ' ' ||
                        COALESCE(regexp_replace(u.phone, '\D', '', 'g'), '')";

        private static readonly Regex PhoneShaped = new(@"^\+?[\d\s\-\.\(\)]+$", RegexOptions.Compiled);
        private static readonly Regex PhoneToken = new(@"^\+?[\d\-\.\(\)]+$", RegexOptions.Compiled);
        private static readonly char[] Separators = { ' ', '\t', ',', '\r', '\n' };

        public static string[] BuildLikePatterns(string? query)
        {
            var trimmed = (query ?? string.Empty).Trim();
            if (trimmed.Length == 0) return Array.Empty<string>();

            var patterns = new List<string>();
            var run = new List<string>();
            void FlushRun()
            {
                if (run.Count == 0) return;
                var digits = NormalizePhoneDigits(string.Concat(run));
                if (digits is not null) patterns.Add($"%{digits}%");
                else patterns.AddRange(run.Select(WordPattern));
                run.Clear();
            }

            foreach (var token in trimmed.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
            {
                if (PhoneToken.IsMatch(token)) { run.Add(token); continue; }
                FlushRun();
                patterns.Add(WordPattern(token));
            }
            FlushRun();

            return patterns.Distinct().Take(MaxWords).ToArray();
        }

        /// <summary>The normalized digits of a query that is entirely a phone number, or null.</summary>
        public static string? PhoneDigits(string? query)
        {
            var trimmed = (query ?? string.Empty).Trim();
            return PhoneShaped.IsMatch(trimmed) ? NormalizePhoneDigits(trimmed) : null;
        }

        private static string? NormalizePhoneDigits(string text)
        {
            var digits = new string(text.Where(char.IsAsciiDigit).ToArray());
            if (digits.Length == 11 && digits[0] == '1') digits = digits[1..];
            return digits.Length >= MinPhoneDigits ? digits : null;
        }

        private static string WordPattern(string word) => $"%{EscapeLike(word.ToLowerInvariant())}%";

        public static string EscapeLike(string value) =>
            value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
    }
}
