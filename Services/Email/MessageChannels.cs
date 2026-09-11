namespace Services.Email
{
    /// <summary>
    /// How a campaign or an automation step goes out. Stored on the row as 'email', 'sms', or
    /// 'both'; a send row is always one of the first two.
    /// </summary>
    public static class MessageChannels
    {
        public const string Email = "email";
        public const string Sms = "sms";
        public const string Both = "both";

        public static readonly string[] All = { Email, Sms, Both };

        public static string Normalize(string? channel)
        {
            var c = (channel ?? "").Trim().ToLowerInvariant();
            return Array.IndexOf(All, c) >= 0 ? c : Email;
        }

        public static bool IncludesEmail(string? channel) => Normalize(channel) is Email or Both;
        public static bool IncludesSms(string? channel) => Normalize(channel) is Sms or Both;

        public static string Label(string? channel) => Normalize(channel) switch
        {
            Sms => "Text",
            Both => "Email and text",
            _ => "Email",
        };
    }

    /// <summary>
    /// The text of a marketing SMS at the last step before Twilio: merge fields already
    /// rendered, opt-out language present, length capped at what Twilio accepts.
    /// </summary>
    public static class SmsText
    {
        /// <summary>Twilio rejects bodies over this many characters.</summary>
        public const int MaxLength = 1600;
        public const string OptOutLine = "Reply STOP to opt out";
        /// <summary>The longest a tenant may write; the opt-out line and a name prefix fit under MaxLength.</summary>
        public const int MaxAuthoredLength = 1000;

        /// <summary>
        /// Marketing texts carry opt-out language by law (TCPA) and by Twilio policy. Added here,
        /// once, so no template can leave it out; not added again when the author already wrote it.
        /// </summary>
        public static string Finish(string rendered)
        {
            var body = (rendered ?? "").Trim();
            if (body.IndexOf("STOP", StringComparison.OrdinalIgnoreCase) < 0)
            {
                body = body.Length == 0 ? OptOutLine : body + "\n" + OptOutLine;
            }
            return body.Length > MaxLength ? body[..MaxLength] : body;
        }
    }
}
