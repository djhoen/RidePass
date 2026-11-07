using System.Security.Cryptography;
using System.Text;
using Services.Repositories.Data.KioskData;
using Services.Repositories.Interfaces;
using webapi.Multitenancy;

namespace webapi.Security
{
    /// <summary>
    /// Front-desk kiosk credentials (Script0310). A kiosk is paired once with a short one-time code a
    /// manager reads off the admin screen; the tablet swaps it for a long-lived random secret, kept in its
    /// localStorage and sent as <see cref="HeaderName"/>. Only SHA-256 hashes are stored (a database leak
    /// can't impersonate a kiosk); revoking the device kills the secret at once. The secret is not a JWT,
    /// so it opens no staff endpoint: only the kiosk API reads it.
    /// </summary>
    public static class KioskCredentials
    {
        public const string HeaderName = "X-Kiosk-Token";
        public static readonly TimeSpan PairingLifetime = TimeSpan.FromMinutes(15);

        // No 0/O, 1/I/L: the code is read off one screen and typed on another.
        private const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
        public const int CodeLength = 8;

        public static string NewSecret() =>
            "kio_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        public static string NewPairingCode()
        {
            var chars = new char[CodeLength];
            for (var i = 0; i < CodeLength; i++) chars[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
            return new string(chars);
        }

        /// <summary>What staff typed, as stored: upper case, letters and digits only ("abcd-2345" = "ABCD2345").</summary>
        public static string NormalizeCode(string? code) =>
            new string((code ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

        public static string Hash(string value) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

        /// <summary>A pairing code is only meaningful at its own tenant.</summary>
        public static string HashPairingCode(Guid tenantId, string code) => Hash($"{tenantId:D}:{NormalizeCode(code)}");

        /// <summary>Shown to staff as XXXX-XXXX.</summary>
        public static string Display(string code) => code.Length == CodeLength ? $"{code[..4]}-{code[4..]}" : code;
    }

    /// <summary>The kiosk calling this request, from its secret (tenant-scoped, not revoked), or null.</summary>
    public interface IKioskAuthenticator
    {
        Task<KioskDevice?> Authenticate(HttpRequest request);
    }

    public class KioskAuthenticator : IKioskAuthenticator
    {
        private readonly IKioskRepository _kiosks;
        private readonly ITenantContext _tenantContext;

        public KioskAuthenticator(IKioskRepository kiosks, ITenantContext tenantContext)
        {
            _kiosks = kiosks;
            _tenantContext = tenantContext;
        }

        public async Task<KioskDevice?> Authenticate(HttpRequest request)
        {
            if (!_tenantContext.IsResolved) return null;
            var secret = request.Headers[KioskCredentials.HeaderName].ToString().Trim();
            if (secret.Length < 20 || !secret.StartsWith("kio_", StringComparison.Ordinal)) return null;
            var device = await _kiosks.GetByCredential(_tenantContext.TenantId, KioskCredentials.Hash(secret));
            if (device is not null) await _kiosks.TouchLastSeen(device.Id, device.TenantId);
            return device;
        }
    }
}
