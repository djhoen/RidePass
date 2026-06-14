using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace Services.Geo
{
    // IP -> country/coords via a no-key hosted lookup (ipwho.is by default).
    //
    // This is deliberately behind IGeoIpService so it can be swapped for the
    // offline MaxMind GeoLite2 reader later (drop a GeoLite2-City.mmdb in, add
    // the MaxMind.GeoIP2 package, and implement IGeoIpService against the reader)
    // without touching any caller. The hosted provider keeps zero ops setup for
    // now at the cost of one outbound call per uncached IP.
    //
    // Results are cached per IP for a day — country/coords for a given IP are
    // stable, and the apex Events page calls this on every visit.
    public class GeoIpService : IGeoIpService
    {
        // Static so connections pool across the (singleton) service lifetime.
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(4) };

        private readonly IMemoryCache _cache;
        private readonly string _urlTemplate;

        public GeoIpService(IMemoryCache cache, IConfiguration config)
        {
            _cache = cache;
            // {ip} is substituted with the resolved client IP.
            _urlTemplate = config["GeoIp:UrlTemplate"] ?? "https://ipwho.is/{ip}";
        }

        public async Task<GeoLocation?> Locate(string? ip, CancellationToken ct = default)
        {
            // No usable public IP (loopback, private, missing) -> can't resolve.
            // Caller decides how to treat an unknown country.
            if (!IsPublicIp(ip)) return null;

            if (_cache.TryGetValue(CacheKey(ip!), out GeoLocation? cached)) return cached;

            try
            {
                var url = _urlTemplate.Replace("{ip}", Uri.EscapeDataString(ip!));
                using var resp = await _http.GetAsync(url, ct);
                if (!resp.IsSuccessStatusCode) return null;

                await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                var root = doc.RootElement;

                // ipwho.is sets success=false on rate-limit / bad-IP errors.
                if (root.TryGetProperty("success", out var ok) &&
                    ok.ValueKind == JsonValueKind.False)
                {
                    return null;
                }

                var result = new GeoLocation
                {
                    CountryCode = GetString(root, "country_code")?.ToUpperInvariant(),
                    Latitude = GetDouble(root, "latitude"),
                    Longitude = GetDouble(root, "longitude"),
                };

                _cache.Set(CacheKey(ip!), result, TimeSpan.FromHours(24));
                return result;
            }
            catch
            {
                // Network/parse failure: degrade gracefully to "unknown".
                return null;
            }
        }

        private static string CacheKey(string ip) => $"geoip:{ip}";

        private static bool IsPublicIp(string? ip)
        {
            if (string.IsNullOrWhiteSpace(ip)) return false;
            if (!IPAddress.TryParse(ip, out var addr)) return false;
            if (IPAddress.IsLoopback(addr)) return false;

            var bytes = addr.GetAddressBytes();
            if (bytes.Length == 4)
            {
                // RFC 1918 private ranges + link-local.
                if (bytes[0] == 10) return false;
                if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return false;
                if (bytes[0] == 192 && bytes[1] == 168) return false;
                if (bytes[0] == 169 && bytes[1] == 254) return false;
            }
            return true;
        }

        private static string? GetString(JsonElement root, string name) =>
            root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
                ? el.GetString()
                : null;

        private static double? GetDouble(JsonElement root, string name) =>
            root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number
                ? el.GetDouble()
                : null;
    }
}
