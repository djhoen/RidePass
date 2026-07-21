using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Services.Helpers;
using Services.Helpers.Interfaces;
using Services.Repositories.Interfaces;

namespace Services.QuickBooks
{
    /// <summary>Result of a code-for-token exchange or a refresh.</summary>
    public record QboTokens(string AccessToken, DateTime AccessExpiresAtUtc, string RefreshToken, DateTime RefreshExpiresAtUtc);

    public interface IQuickBooksTokenService
    {
        bool IsConfigured { get; }
        /// <summary>The Intuit consent URL to send the tenant's browser to.</summary>
        string BuildAuthorizationUrl(string state);
        /// <summary>Trade the ?code= from the callback for a token pair. Null if Intuit rejects it.</summary>
        Task<QboTokens?> ExchangeCodeAsync(string code, CancellationToken ct = default);
        /// <summary>
        /// A usable access token for this tenant, refreshing and re-persisting if the cached one is
        /// stale. Null when the tenant isn't connected or the link is dead, callers must treat null
        /// as "not connected", never retry it into a loop.
        /// </summary>
        Task<string?> GetAccessTokenAsync(Guid tenantId, CancellationToken ct = default);
        /// <summary>Best-effort revoke at Intuit so the tenant's grant list doesn't keep a dead app.</summary>
        Task RevokeAsync(Guid tenantId, CancellationToken ct = default);
    }

    /// <summary>
    /// Owns the Intuit OAuth2 authorization-code flow and the token lifecycle. This is the only
    /// class that decrypts a QuickBooks token; everything else asks it for an access token.
    ///
    /// Intuit's tokens behave differently from most: the access token lives ~1 hour, and the refresh
    /// token lives ~100 days but is REPLACED on most refreshes. So a refresh isn't read-only, it
    /// mutates stored state, and losing the new refresh token bricks the connection until the tenant
    /// re-authorises. Two consequences shape the code below:
    ///
    ///   • Refresh is serialised per tenant with a Postgres advisory lock. Without it, the webapi
    ///     and the TaskRunner refreshing at the same moment would each persist a different rotated
    ///     token, and whichever lost the write would already have been invalidated at Intuit.
    ///   • The lock is taken BEFORE the staleness re-check, so the loser of a race re-reads the row
    ///     and finds the token the winner just stored, rather than burning a second refresh.
    /// </summary>
    public class QuickBooksTokenService : IQuickBooksTokenService
    {
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
        /// <summary>Refresh this far ahead of expiry so a token can't die mid-request.</summary>
        private static readonly TimeSpan ExpiryGrace = TimeSpan.FromMinutes(5);

        private readonly QuickBooksOptions _options;
        private readonly IQuickBooksRepository _repo;
        private readonly IDbHelper _db;
        private readonly ILogger<QuickBooksTokenService> _logger;

        public QuickBooksTokenService(
            QuickBooksOptions options,
            IQuickBooksRepository repo,
            IDbHelper db,
            ILogger<QuickBooksTokenService> logger)
        {
            _options = options;
            _repo = repo;
            _db = db;
            _logger = logger;
        }

        public bool IsConfigured => _options.IsConfigured;

        public string BuildAuthorizationUrl(string state)
        {
            var q = new Dictionary<string, string?>
            {
                ["client_id"]     = _options.ClientId,
                ["response_type"] = "code",
                ["scope"]         = QuickBooksOptions.Scope,
                ["redirect_uri"]  = _options.RedirectUri,
                ["state"]         = state,
            };
            var query = string.Join("&", q.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value ?? "")}"));
            return $"{QuickBooksOptions.AuthorizeUrl}?{query}";
        }

        public Task<QboTokens?> ExchangeCodeAsync(string code, CancellationToken ct = default) =>
            PostTokenRequestAsync(new Dictionary<string, string>
            {
                ["grant_type"]   = "authorization_code",
                ["code"]         = code,
                ["redirect_uri"] = _options.RedirectUri!,
            }, ct);

        public async Task<string?> GetAccessTokenAsync(Guid tenantId, CancellationToken ct = default)
        {
            if (!IsConfigured) return null;

            var conn = await _repo.GetConnection(tenantId);
            if (conn is null || conn.Status is "revoked") return null;

            // Fast path: a cached access token with life left in it. No lock, no network.
            var cached = TryDecrypt(conn.AccessTokenEncrypted);
            if (cached is not null && conn.AccessTokenExpiresAtUtc > DateTime.UtcNow.Add(ExpiryGrace))
            {
                return cached;
            }

            // Slow path. Serialise per tenant: a rotated refresh token invalidates the old one at
            // Intuit, so two concurrent refreshes would leave one process holding a dead token and
            // could persist the wrong one.
            await using var _ = await _db.AcquireAdvisoryLock($"qbo-token-refresh-{tenantId}");

            // Re-read under the lock, the process we queued behind has probably just refreshed.
            conn = await _repo.GetConnection(tenantId);
            if (conn is null || conn.Status is "revoked") return null;

            cached = TryDecrypt(conn.AccessTokenEncrypted);
            if (cached is not null && conn.AccessTokenExpiresAtUtc > DateTime.UtcNow.Add(ExpiryGrace))
            {
                return cached;
            }

            var refreshToken = TryDecrypt(conn.RefreshTokenEncrypted);
            if (refreshToken is null)
            {
                // Almost always an encryption key rotation: the ciphertext is intact but no longer
                // decryptable. Say so plainly, "invalid token" would send someone hunting at Intuit.
                await _repo.SetStatus(tenantId, "error",
                    "Stored QuickBooks credentials could not be read. Disconnect and reconnect QuickBooks.");
                _logger.LogError("QBO refresh token for tenant {TenantId} failed to decrypt (encryption key rotated?)", tenantId);
                return null;
            }

            if (conn.RefreshTokenExpiresAtUtc is { } refreshExpiry && refreshExpiry <= DateTime.UtcNow)
            {
                await _repo.SetStatus(tenantId, "expired",
                    "The QuickBooks connection expired. Reconnect QuickBooks to resume syncing.");
                return null;
            }

            var tokens = await PostTokenRequestAsync(new Dictionary<string, string>
            {
                ["grant_type"]    = "refresh_token",
                ["refresh_token"] = refreshToken,
            }, ct);

            if (tokens is null)
            {
                await _repo.SetStatus(tenantId, "expired",
                    "QuickBooks rejected the stored credentials. Reconnect QuickBooks to resume syncing.");
                return null;
            }

            await _repo.UpdateTokens(
                tenantId,
                EncryptionHelper.Encrypt(tokens.RefreshToken, null),
                tokens.RefreshExpiresAtUtc,
                EncryptionHelper.Encrypt(tokens.AccessToken, null),
                tokens.AccessExpiresAtUtc);

            return tokens.AccessToken;
        }

        public async Task RevokeAsync(Guid tenantId, CancellationToken ct = default)
        {
            if (!IsConfigured) return;
            var conn = await _repo.GetConnection(tenantId);
            var refreshToken = TryDecrypt(conn?.RefreshTokenEncrypted);
            if (refreshToken is null) return;

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, QuickBooksOptions.RevokeUrl);
                req.Headers.Authorization = BasicAuth();
                req.Content = new StringContent(
                    JsonSerializer.Serialize(new { token = refreshToken }), Encoding.UTF8, "application/json");
                await _http.SendAsync(req, ct);
            }
            catch (Exception ex)
            {
                // The local row is being deleted regardless, a failed revoke just leaves a stale
                // grant in the tenant's Intuit account, which they can remove themselves.
                _logger.LogWarning(ex, "QBO token revoke failed for tenant {TenantId}; disconnecting locally anyway", tenantId);
            }
        }

        private async Task<QboTokens?> PostTokenRequestAsync(Dictionary<string, string> form, CancellationToken ct)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, QuickBooksOptions.TokenUrl);
                req.Headers.Authorization = BasicAuth();
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                req.Content = new FormUrlEncodedContent(form);

                using var resp = await _http.SendAsync(req, ct);
                var body = await resp.Content.ReadAsStringAsync(ct);
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("QBO token request failed: {Status} {Body}", (int)resp.StatusCode, body);
                    return null;
                }

                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                var access = root.TryGetProperty("access_token", out var a) ? a.GetString() : null;
                var refresh = root.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
                if (string.IsNullOrEmpty(access) || string.IsNullOrEmpty(refresh)) return null;

                var now = DateTime.UtcNow;
                var accessLife  = root.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var s) ? s : 3600;
                var refreshLife = root.TryGetProperty("x_refresh_token_expires_in", out var xe) && xe.TryGetInt32(out var xs) ? xs : 8_726_400;

                return new QboTokens(access, now.AddSeconds(accessLife), refresh, now.AddSeconds(refreshLife));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "QBO token request threw");
                return null;
            }
        }

        private AuthenticationHeaderValue BasicAuth() =>
            new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}")));

        /// <summary>EncryptionHelper.Decrypt returns null on a bad key or corrupt blob rather than throwing.</summary>
        private static string? TryDecrypt(string? cipher) =>
            string.IsNullOrEmpty(cipher) ? null : EncryptionHelper.Decrypt(cipher);
    }
}
