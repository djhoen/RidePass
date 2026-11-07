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
        private readonly KioskCredentialCache _known;

        public KioskAuthenticator(IKioskRepository kiosks, ITenantContext tenantContext, KioskCredentialCache known)
        {
            _kiosks = kiosks;
            _tenantContext = tenantContext;
            _known = known;
        }

        public async Task<KioskDevice?> Authenticate(HttpRequest request)
        {
            if (!_tenantContext.IsResolved) return null;
            var secret = request.Headers[KioskCredentials.HeaderName].ToString().Trim();
            if (secret.Length < 20 || !secret.StartsWith("kio_", StringComparison.Ordinal)) return null;
            var hash = KioskCredentials.Hash(secret);
            var device = await _kiosks.GetByCredential(_tenantContext.TenantId, hash);
            if (device is null)
            {
                _known.Forget(hash);
                return null;
            }
            _known.MarkValid(hash);
            await _kiosks.TouchLastSeen(device.Id, device.TenantId);
            return device;
        }
    }

    /// <summary>
    /// Credential hashes recently proven valid (a few minutes), so the rate limiter can give a real kiosk its
    /// own partition without a database call; anything unknown or invalid is limited by client address
    /// instead, so made-up tokens can't each buy a fresh allowance. Revoking a kiosk forgets its hash.
    /// </summary>
    public class KioskCredentialCache
    {
        private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _valid = new();

        public void MarkValid(string hash) => _valid[hash] = DateTime.UtcNow.Add(Lifetime);

        public void Forget(string? hash)
        {
            if (hash is not null) _valid.TryRemove(hash, out _);
        }

        public bool IsKnown(string hash)
        {
            if (!_valid.TryGetValue(hash, out var until)) return false;
            if (until > DateTime.UtcNow) return true;
            _valid.TryRemove(hash, out _);
            return false;
        }

        /// <summary>The rate-limit partition for a request: the device when its key is known-valid, else the address.</summary>
        public string PartitionFor(HttpContext ctx)
        {
            var secret = ctx.Request.Headers[KioskCredentials.HeaderName].ToString().Trim();
            if (secret.Length > 0)
            {
                var hash = KioskCredentials.Hash(secret);
                if (IsKnown(hash)) return "kiosk:" + hash;
            }
            return "kiosk-ip:" + (ctx.Connection.RemoteIpAddress?.ToString() ?? "anonymous");
        }
    }
}
