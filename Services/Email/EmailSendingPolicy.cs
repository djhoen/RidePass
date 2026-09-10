using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace Services.Email
{
    /// <summary>
    /// Which From addresses the platform may put on an outbound email.
    ///
    /// SendGrid DKIM-signs for exactly one domain (the one behind Email:FromAddress, ridepass.io),
    /// and DMARC aligns on the organizational domain, so any address under it (including a
    /// per-track subdomain such as noreply@highland.ridepass.io) is deliverable, while a track's
    /// own domain is not until it has been authenticated separately. A tenant's configurable
    /// sending address is therefore restricted to "&lt;anything&gt;@&lt;tenant-subdomain&gt;.&lt;sending domain&gt;":
    /// branded, DMARC-safe, and impossible to confuse with a platform address like
    /// billing@ridepass.io. Per-tenant authenticated domains can layer on top later as an
    /// override that this policy would also accept once verified.
    /// </summary>
    public static class EmailSendingPolicy
    {
        // Local part per the practical subset of RFC 5322 (no quoted forms), then a hostname.
        private static readonly Regex AddressShape = new(
            @"^(?<local>[A-Za-z0-9._%+\-]{1,64})@(?<domain>[A-Za-z0-9](?:[A-Za-z0-9\-]{0,61}[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9\-]{0,61}[A-Za-z0-9])?)+)$",
            RegexOptions.Compiled);

        /// <summary>The domain SendGrid signs for. Email:SendingDomain when set, else the domain of Email:FromAddress.</summary>
        public static string SendingDomain(IConfiguration config)
        {
            var explicitDomain = config["Email:SendingDomain"];
            if (!string.IsNullOrWhiteSpace(explicitDomain)) return explicitDomain.Trim().ToLowerInvariant();
            var from = config["Email:FromAddress"] ?? string.Empty;
            var at = from.LastIndexOf('@');
            return at >= 0 && at < from.Length - 1 ? from[(at + 1)..].Trim().ToLowerInvariant() : "ridepass.io";
        }

        /// <summary>The domain a tenant's sending address must use: &lt;subdomain&gt;.&lt;sending domain&gt;.</summary>
        public static string TenantDomain(string tenantSubdomain, string sendingDomain)
            => $"{tenantSubdomain.Trim().ToLowerInvariant()}.{sendingDomain}";

        /// <summary>Trim, lower-case the domain part, null when blank. Does not validate.</summary>
        public static string? Normalize(string? address)
        {
            if (string.IsNullOrWhiteSpace(address)) return null;
            var s = address.Trim();
            var at = s.LastIndexOf('@');
            return at < 0 ? s : s[..at] + "@" + s[(at + 1)..].ToLowerInvariant();
        }

        /// <summary>Well-formed and exactly at the tenant's own subdomain of the sending domain.</summary>
        public static bool IsValidTenantFromAddress(string address, string tenantSubdomain, string sendingDomain)
        {
            var m = AddressShape.Match(address.Trim());
            if (!m.Success) return false;
            return string.Equals(m.Groups["domain"].Value, TenantDomain(tenantSubdomain, sendingDomain),
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The send-time invariant: the From domain is the sending domain or a subdomain of it.
        /// Anything else would go out unsigned for its domain and fail DMARC, so the mailer falls back.</summary>
        public static bool IsUnderSendingDomain(string address, string sendingDomain)
        {
            var m = AddressShape.Match(address.Trim());
            if (!m.Success) return false;
            var domain = m.Groups["domain"].Value;
            return string.Equals(domain, sendingDomain, StringComparison.OrdinalIgnoreCase)
                || domain.EndsWith("." + sendingDomain, StringComparison.OrdinalIgnoreCase);
        }
    }
}
