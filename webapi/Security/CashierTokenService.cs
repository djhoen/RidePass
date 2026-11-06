using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace webapi.Security
{
    // The cashier token (Script0298 staff codes): once a staff member's POS code has been
    // verified, the register holds an opaque token that says "this person is ringing" and sends
    // it with every sale / tab action, so the sale is attributed to them and not to whichever
    // account is signed in on the terminal. It is ASP.NET Data Protection over
    // "{tenantId}|{userId}|{expUnix}": tamper-proof, tenant-bound, and short-lived (a long shift,
    // not a session). It carries no permission of its own: the terminal account still has to
    // pass the endpoint's policy; the token only changes who the sale is attributed to.
    public interface ICashierTokenService
    {
        // The one message every endpoint returns for a token that does not validate (missing when
        // required, tampered, another tenant's, expired, or naming a user no longer here). The
        // register matches it on "staff code" to reopen the keypad, so that phrase stays.
        const string InvalidTokenMessage = "Your staff code session expired. Enter your code again.";

        (string Token, DateTime ExpiresAtUtc) Issue(Guid tenantId, Guid userId);
        CashierTokenResult Validate(string? token, Guid tenantId);
    }

    public sealed class CashierTokenResult
    {
        public Guid? UserId { get; init; }
        public DateTime? ExpiresAtUtc { get; init; }
        public string? Error { get; init; }
        public bool IsValid => UserId.HasValue;

        public static CashierTokenResult Invalid(string error) => new() { Error = error };
    }

    public class CashierTokenService : ICashierTokenService
    {
        public const string Purpose = "ridepass.cashier";
        public static readonly TimeSpan Lifetime = TimeSpan.FromHours(14);

        private readonly IDataProtector _protector;

        public CashierTokenService(IDataProtectionProvider provider)
        {
            _protector = provider.CreateProtector(Purpose);
        }

        public (string Token, DateTime ExpiresAtUtc) Issue(Guid tenantId, Guid userId) => Issue(tenantId, userId, Lifetime);

        // Lifetime is a parameter so a test can mint an already-expired token; callers in the
        // app use the one above.
        public (string Token, DateTime ExpiresAtUtc) Issue(Guid tenantId, Guid userId, TimeSpan lifetime)
        {
            var expUnix = DateTimeOffset.UtcNow.Add(lifetime).ToUnixTimeSeconds();
            var payload = string.Create(CultureInfo.InvariantCulture, $"{tenantId:D}|{userId:D}|{expUnix}");
            return (_protector.Protect(payload), DateTimeOffset.FromUnixTimeSeconds(expUnix).UtcDateTime);
        }

        public CashierTokenResult Validate(string? token, Guid tenantId)
        {
            if (string.IsNullOrWhiteSpace(token)) return CashierTokenResult.Invalid("No staff code session.");
            string payload;
            try { payload = _protector.Unprotect(token.Trim()); }
            catch (CryptographicException) { return CashierTokenResult.Invalid("Staff code session not recognized."); }

            var parts = payload.Split('|');
            if (parts.Length != 3
                || !Guid.TryParseExact(parts[0], "D", out var tokenTenant)
                || !Guid.TryParseExact(parts[1], "D", out var userId)
                || !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var expUnix))
                return CashierTokenResult.Invalid("Staff code session not recognized.");
            // A token minted at another tenant is worthless here even though it unprotects: the
            // key ring is shared across the host, the tenant is not.
            if (tokenTenant != tenantId) return CashierTokenResult.Invalid("Staff code session belongs to another venue.");
            var expiresAt = DateTimeOffset.FromUnixTimeSeconds(expUnix).UtcDateTime;
            if (expiresAt <= DateTime.UtcNow) return CashierTokenResult.Invalid("Staff code session expired.");
            return new CashierTokenResult { UserId = userId, ExpiresAtUtc = expiresAt };
        }
    }
}
