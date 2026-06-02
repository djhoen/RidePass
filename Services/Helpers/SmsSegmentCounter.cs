namespace Services.Helpers
{
    /// <summary>
    /// Counts the number of carrier-billable SMS segments a message body will use.
    /// Twilio (and every other SMS gateway) bills per segment, not per "logical
    /// message": a 160-char GSM-7 message is 1 segment, a 161-char message is 2
    /// segments (because concatenated SMS reserves 7 chars per part for the UDH
    /// reassembly header, dropping the per-part limit to 153). Same shape for
    /// UCS-2 (used the moment any non-GSM character — emoji, accented chars
    /// beyond a small allowlist, anything non-Latin — appears): 70 chars single,
    /// 67 chars per concatenated part.
    ///
    /// We count segments client-side so the campaign compose UI can show live
    /// cost ("167 chars · 2 segments · $33.88 for 847 recipients") and the
    /// confirm dialog can show the exact send total. The numbers must agree
    /// with what Twilio later bills us; we verify by reconciling against the
    /// authoritative Price field on the StatusCallback webhook (see the billing
    /// ledger design).
    /// </summary>
    public static class SmsSegmentCounter
    {
        // GSM 03.38 basic alphabet. Each character occupies 7 bits = 1 "septet".
        private static readonly HashSet<char> GsmBasic = new(
            "@£$¥èéùìòÇ\nØø\rÅåΔ_ΦΓΛΩΠΨΣΘΞÆæßÉ !\"#¤%&'()*+,-./0123456789:;<=>?¡" +
            "ABCDEFGHIJKLMNOPQRSTUVWXYZÄÖÑÜ§¿abcdefghijklmnopqrstuvwxyzäöñüà");

        // GSM 03.38 extension set. Each of these characters occupies 14 bits = 2
        // septets because the encoder must emit an ESC (0x1B) before the char.
        private static readonly HashSet<char> GsmExtended = new("\f^{}\\[~]|€");

        public enum Encoding
        {
            Gsm7,
            Ucs2,
        }

        public record Result(int Segments, int CharacterCount, Encoding Encoding);

        public static Result Count(string? body)
        {
            if (string.IsNullOrEmpty(body))
                return new Result(0, 0, Encoding.Gsm7);

            var useUcs2 = body.Any(c => !GsmBasic.Contains(c) && !GsmExtended.Contains(c));

            if (!useUcs2)
            {
                // GSM-7: extended chars cost 2 septets each.
                var septets = body.Sum(c => GsmExtended.Contains(c) ? 2 : 1);
                var segments = septets <= 160 ? 1 : (int)Math.Ceiling(septets / 153.0);
                return new Result(segments, septets, Encoding.Gsm7);
            }
            else
            {
                // UCS-2: count UTF-16 code units (surrogate pairs already count as 2,
                // which matches how Twilio bills emoji).
                var units = body.Length;
                var segments = units <= 70 ? 1 : (int)Math.Ceiling(units / 67.0);
                return new Result(segments, units, Encoding.Ucs2);
            }
        }
    }
}
