using System.Text.RegularExpressions;

namespace Services.Embed
{
    /// <summary>
    /// Builds the <c>frame-ancestors</c> CSP value that controls which parent sites may
    /// frame a tenant's embedded widgets. Shared by the public CSP endpoint (stamped by
    /// nginx onto /embed responses) and the branding response (client-side guard UX).
    ///
    /// Effective allow-list = global first-party origins (always) ∪ a tenant's own
    /// allowed origins (only when the tenant has embedding enabled).
    /// </summary>
    public static class EmbedPolicy
    {
        // A valid CSP host-source: scheme + host, optional single leading wildcard label,
        // optional port. Deliberately strict so a setting value can never inject extra
        // header directives (no spaces, semicolons, or control chars get through).
        private static readonly Regex SourcePattern =
            new(@"^https?://(\*\.)?[a-z0-9.-]+(:\d+)?$", RegexOptions.Compiled);

        /// <summary>Split a stored/submitted blob into individual origin tokens.</summary>
        public static List<string> ParseOrigins(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return new List<string>();
            return raw
                .Split(new[] { '\n', '\r', ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();
        }

        /// <summary>
        /// Expand one entered origin into the valid CSP source(s) it should authorize.
        /// Rules:
        ///   - A missing scheme defaults to https.
        ///   - Any path/query is dropped ("https://x.com/foo" -> "https://x.com").
        ///   - An apex domain (two labels, e.g. "xyz.com") also authorizes "www.xyz.com",
        ///     and a "www." host also authorizes its apex. So adding "xyz.com" covers both
        ///     https://xyz.com and https://www.xyz.com.
        ///   - A wildcard host ("*.xyz.com") is kept as-is (it already covers subdomains).
        /// Returns an empty list if the entry isn't a safe, well-formed origin.
        /// </summary>
        public static List<string> ExpandSource(string? origin)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(origin)) return result;
            var s = origin.Trim().ToLowerInvariant();

            var scheme = "https";
            var schemeIdx = s.IndexOf("://", StringComparison.Ordinal);
            if (schemeIdx >= 0)
            {
                var sch = s.Substring(0, schemeIdx);
                if (sch != "http" && sch != "https") return result;   // unsupported scheme
                scheme = sch;
                s = s.Substring(schemeIdx + 3);
            }

            // Drop any path/query/fragment.
            var cut = s.IndexOfAny(new[] { '/', '?', '#' });
            if (cut >= 0) s = s.Substring(0, cut);
            if (s.Length == 0) return result;

            // Split off an optional :port so the www/apex toggle works on the host alone.
            string host = s, port = "";
            var colon = s.IndexOf(':');
            if (colon >= 0)
            {
                host = s.Substring(0, colon);
                port = s.Substring(colon);
            }

            var hosts = new List<string> { host };
            if (host.StartsWith("*."))
            {
                // wildcard already spans subdomains; no apex/www toggle
            }
            else if (host.StartsWith("www."))
            {
                hosts.Add(host.Substring(4));                       // apex counterpart
            }
            else if (host.Count(c => c == '.') == 1)
            {
                hosts.Add("www." + host);                           // www counterpart of an apex
            }

            foreach (var h in hosts)
            {
                var src = $"{scheme}://{h}{port}";
                if (SourcePattern.IsMatch(src) && !result.Contains(src)) result.Add(src);
            }
            return result;
        }

        /// <summary>Normalize + expand + de-dupe a list of origins, dropping invalid entries.</summary>
        public static List<string> NormalizeList(IEnumerable<string>? origins)
        {
            var set = new List<string>();
            if (origins == null) return set;
            foreach (var o in origins)
                foreach (var src in ExpandSource(o))
                    if (!set.Contains(src)) set.Add(src);
            return set;
        }

        /// <summary>
        /// The effective, normalized list of origins allowed to frame this tenant's widgets.
        /// Global origins always apply; the tenant's own origins apply only when embedding
        /// is enabled. Returns an empty list when nothing is allowed.
        /// </summary>
        public static List<string> EffectiveOrigins(
            IEnumerable<string>? globalOrigins, IEnumerable<string>? tenantOrigins, bool tenantEmbedEnabled)
        {
            var set = NormalizeList(globalOrigins);
            if (tenantEmbedEnabled)
            {
                foreach (var o in NormalizeList(tenantOrigins))
                    if (!set.Contains(o)) set.Add(o);
            }
            return set;
        }

        /// <summary>
        /// The full CSP frame-ancestors value. Empty allow-list yields <c>'none'</c>
        /// (block all framing). Never returns an empty string.
        /// </summary>
        public static string BuildFrameAncestors(
            IEnumerable<string>? globalOrigins, IEnumerable<string>? tenantOrigins, bool tenantEmbedEnabled)
        {
            var origins = EffectiveOrigins(globalOrigins, tenantOrigins, tenantEmbedEnabled);
            return origins.Count == 0 ? "'none'" : string.Join(' ', origins);
        }
    }
}
