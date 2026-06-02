using System.Security.Cryptography;
using System.Text;

namespace Services.Sms
{
    /// <summary>
    /// Validates the X-Twilio-Signature header on incoming webhook requests.
    /// Twilio signs each request with HMAC-SHA1 of (full request URL +
    /// concatenated form params sorted ordinally by key), keyed by the auth
    /// token of the account that owns the resource. For our per-tenant
    /// subaccount-owned numbers, that's the SUBACCOUNT auth token — which we
    /// look up by AccountSid in the form body, decrypt, then pass here.
    ///
    /// The URL passed in must match what Twilio used. Behind a TLS-terminating
    /// reverse proxy, Request.Scheme is "http" while Twilio called "https";
    /// pass the canonical configured URL (Sms:Twilio:StatusCallbackUrl) rather
    /// than reconstructing from the request.
    /// </summary>
    public static class TwilioSignatureValidator
    {
        public static bool Verify(
            string authToken,
            string url,
            IEnumerable<KeyValuePair<string, string>> formParams,
            string? signatureHeader)
        {
            if (string.IsNullOrEmpty(signatureHeader)) return false;
            if (string.IsNullOrEmpty(authToken)) return false;

            var sb = new StringBuilder(url);
            foreach (var kv in formParams.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                sb.Append(kv.Key);
                sb.Append(kv.Value);
            }

            using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(authToken));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
            var expected = Convert.ToBase64String(hash);

            var expectedBytes = Encoding.UTF8.GetBytes(expected);
            var providedBytes = Encoding.UTF8.GetBytes(signatureHeader);
            if (expectedBytes.Length != providedBytes.Length) return false;
            return CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
        }
    }
}
