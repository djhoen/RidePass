namespace Services.Helpers
{
    /// <summary>
    /// Inbound SMS keyword recognizer for compliance handling. Matches the
    /// canonical keyword sets that US/Canada carriers and Twilio recognize:
    ///   • Opt-out: STOP, STOPALL, UNSUBSCRIBE, CANCEL, END, QUIT
    ///   • Opt-in:  START, UNSTOP, YES
    ///   • Help:    HELP, INFO
    ///
    /// Matching is whole-body, trimmed, case-insensitive — matching how the
    /// carriers themselves filter. A message like "please STOP texting me"
    /// is intentionally NOT a STOP keyword by this rule; only the bare word
    /// counts. That mirrors carrier behavior and avoids accidental opt-outs
    /// from prose that happens to mention the word.
    /// </summary>
    public enum SmsKeyword
    {
        None,
        OptOut,
        OptIn,
        Help,
    }

    public static class SmsKeywords
    {
        private static readonly HashSet<string> OptOutWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "STOP", "STOPALL", "UNSUBSCRIBE", "CANCEL", "END", "QUIT",
        };

        private static readonly HashSet<string> OptInWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "START", "UNSTOP", "YES",
        };

        private static readonly HashSet<string> HelpWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "HELP", "INFO",
        };

        /// <summary>
        /// Classify an inbound message body. Returns (None, null) when the
        /// body isn't a recognized keyword. When a keyword IS matched, the
        /// returned canonical form is the matched keyword in uppercase so
        /// it can be stored in last_keyword without each caller re-trimming.
        /// </summary>
        public static (SmsKeyword Kind, string? Canonical) Classify(string? body)
        {
            if (string.IsNullOrWhiteSpace(body)) return (SmsKeyword.None, null);
            var word = body.Trim();
            if (word.Length == 0) return (SmsKeyword.None, null);

            if (OptOutWords.Contains(word)) return (SmsKeyword.OptOut, word.ToUpperInvariant());
            if (OptInWords.Contains(word)) return (SmsKeyword.OptIn, word.ToUpperInvariant());
            if (HelpWords.Contains(word)) return (SmsKeyword.Help, word.ToUpperInvariant());

            return (SmsKeyword.None, null);
        }
    }
}
