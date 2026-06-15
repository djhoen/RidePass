using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Services.Helpers.Interfaces;

namespace Services.Helpers
{
    /// <summary>
    /// Signs and verifies the opaque tokens carried in email links (currently one-click
    /// unsubscribe). Format: base64url(payload) + "." + base64url(HMAC-SHA256(payload)),
    /// where payload is "{tenantId}|{email}". Stateless and tamper-proof: a recipient can't
    /// edit the address or tenant without invalidating the signature.
    /// </summary>
    public class EmailLinkTokens : IEmailLinkTokens
    {
        private readonly byte[] _key;

        public EmailLinkTokens(IConfiguration config)
        {
            // Dedicated key if set, otherwise piggyback on the JWT secret (high-entropy and
            // already required at startup) so this needs no new env var to function.
            var keyMaterial = config["Email:UnsubscribeSigningKey"]
                ?? config["Jwt:SigningKey"]
                ?? throw new InvalidOperationException("No signing key available for email link tokens.");
            _key = Encoding.UTF8.GetBytes(keyMaterial);
        }

        public string GenerateUnsubscribe(Guid? tenantId, string email)
        {
            var payload = $"{(tenantId.HasValue ? tenantId.Value.ToString() : "")}|{email}";
            var payloadBytes = Encoding.UTF8.GetBytes(payload);
            var sig = Sign(payloadBytes);
            return $"{Base64Url(payloadBytes)}.{Base64Url(sig)}";
        }

        public bool TryParseUnsubscribe(string token, out Guid? tenantId, out string email)
        {
            tenantId = null;
            email = string.Empty;
            if (string.IsNullOrWhiteSpace(token)) return false;

            var parts = token.Split('.');
            if (parts.Length != 2) return false;

            byte[] payloadBytes, sig;
            try
            {
                payloadBytes = FromBase64Url(parts[0]);
                sig = FromBase64Url(parts[1]);
            }
            catch
            {
                return false;
            }

            // Constant-time comparison against a forged signature.
            var expected = Sign(payloadBytes);
            if (!CryptographicOperations.FixedTimeEquals(sig, expected)) return false;

            var payload = Encoding.UTF8.GetString(payloadBytes);
            var sep = payload.IndexOf('|');
            if (sep < 0) return false;

            var tenantPart = payload.Substring(0, sep);
            email = payload.Substring(sep + 1);
            if (string.IsNullOrEmpty(email)) return false;

            if (!string.IsNullOrEmpty(tenantPart))
            {
                if (!Guid.TryParse(tenantPart, out var t)) return false;
                tenantId = t;
            }
            return true;
        }

        private byte[] Sign(byte[] data)
        {
            using var hmac = new HMACSHA256(_key);
            return hmac.ComputeHash(data);
        }

        private static string Base64Url(byte[] data) =>
            Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static byte[] FromBase64Url(string s)
        {
            var padded = s.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2: padded += "=="; break;
                case 3: padded += "="; break;
            }
            return Convert.FromBase64String(padded);
        }
    }
}
