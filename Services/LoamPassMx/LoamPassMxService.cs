using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Services.LoamPassMx
{
    /// <summary>
    /// Calls the LoamMx partner API (/RidePassIntegration/*), authenticating with a shared
    /// X-Api-Key. LoamMx wraps every response in an envelope { status, data, message, error };
    /// we read `data` on success and `error` on failure.
    /// </summary>
    public class LoamPassMxService : ILoamPassMxService
    {
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

        private readonly string? _baseUrl;
        private readonly string? _apiKey;
        private readonly ILogger<LoamPassMxService> _logger;

        public LoamPassMxService(IConfiguration config, ILogger<LoamPassMxService> logger)
        {
            _baseUrl = NullIfEmpty(config["LoamPassMx:BaseUrl"]);
            _apiKey = NullIfEmpty(config["LoamPassMx:ApiKey"]);
            _logger = logger;
        }

        public bool IsConfigured => !string.IsNullOrEmpty(_baseUrl) && !string.IsNullOrEmpty(_apiKey);

        public async Task<bool> VerifyStartAsync(string email, CancellationToken ct = default)
        {
            var resp = await PostAsync("VerifyStart", new { email }, ct);
            return resp is { Success: true };
        }

        public async Task<LoamPassAccount?> VerifyConfirmAsync(string email, string code, CancellationToken ct = default)
        {
            var resp = await PostAsync("VerifyConfirm", new { email, code }, ct);
            if (resp is not { Success: true, Data: { } data }) return null;
            var accountId = GetString(data, "accountId");
            if (string.IsNullOrEmpty(accountId)) return null;
            return new LoamPassAccount
            {
                AccountId = accountId,
                Email = GetString(data, "email"),
                DisplayName = GetString(data, "displayName"),
            };
        }

        public async Task<int> GetCreditsAsync(string accountId, string destinationId, CancellationToken ct = default)
        {
            if (!IsConfigured) return 0;
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get,
                    $"{Root()}/Credits?accountId={Uri.EscapeDataString(accountId)}&destinationId={Uri.EscapeDataString(destinationId)}");
                req.Headers.Add("X-Api-Key", _apiKey!);
                using var httpResp = await _http.SendAsync(req, ct);
                var parsed = await ParseAsync(httpResp, ct);
                if (parsed is not { Success: true, Data: { } data }) return 0;
                return GetInt(data, "creditsAvailable");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LoamPassMx GetCredits failed");
                return 0;
            }
        }

        public async Task<LoamPassRedeemResult> RedeemAsync(string accountId, string destinationId, string idempotencyKey, CancellationToken ct = default)
        {
            if (!IsConfigured)
                return new LoamPassRedeemResult { Redeemed = false, Error = "LoamPassMx integration is not configured." };

            var resp = await PostAsync("Redeem", new { accountId, destinationId, idempotencyKey }, ct);
            if (resp is null)
                return new LoamPassRedeemResult { Redeemed = false, Error = "Could not reach LoamPassMx." };
            if (!resp.Value.Success)
                return new LoamPassRedeemResult { Redeemed = false, Error = resp.Value.Error ?? "Redemption was declined." };

            var data = resp.Value.Data;
            return new LoamPassRedeemResult
            {
                Redeemed = data is { } d && GetBool(d, "redeemed"),
                AlreadyProcessed = data is { } d2 && GetBool(d2, "alreadyProcessed"),
                Remaining = data is { } d3 ? GetInt(d3, "remaining") : 0,
            };
        }

        public async Task<bool> RefundAsync(string idempotencyKey, CancellationToken ct = default)
        {
            var resp = await PostAsync("Unredeem", new { idempotencyKey }, ct);
            return resp is { Success: true };
        }

        public async Task<LoamPassAccount?> GetPassOwnerAsync(string passId, CancellationToken ct = default)
        {
            if (!IsConfigured) return null;
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get,
                    $"{Root()}/PassOwner?passId={Uri.EscapeDataString(passId)}");
                req.Headers.Add("X-Api-Key", _apiKey!);
                using var httpResp = await _http.SendAsync(req, ct);
                var parsed = await ParseAsync(httpResp, ct);
                if (parsed is not { Success: true, Data: { } data }) return null;
                var accountId = GetString(data, "accountId");
                if (string.IsNullOrEmpty(accountId)) return null;
                return new LoamPassAccount
                {
                    AccountId = accountId,
                    Email = GetString(data, "email"),
                    DisplayName = GetString(data, "displayName"),
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LoamPassMx GetPassOwner failed");
                return null;
            }
        }

        // ---- helpers ----

        private string Root() => $"{_baseUrl!.TrimEnd('/')}/RidePassIntegration";

        private readonly record struct Envelope(bool Success, JsonElement? Data, string? Error);

        private async Task<Envelope?> PostAsync(string path, object body, CancellationToken ct)
        {
            if (!IsConfigured) return null;
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, $"{Root()}/{path}")
                {
                    Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
                };
                req.Headers.Add("X-Api-Key", _apiKey!);
                using var resp = await _http.SendAsync(req, ct);
                return await ParseAsync(resp, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LoamPassMx POST {Path} failed", path);
                return null;
            }
        }

        private static async Task<Envelope?> ParseAsync(HttpResponseMessage resp, CancellationToken ct)
        {
            JsonElement? data = null;
            string? error = null;
            try
            {
                await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                var root = doc.RootElement;
                if (root.TryGetProperty("data", out var d) && d.ValueKind != JsonValueKind.Null)
                    data = d.Clone();   // detach so it survives the JsonDocument dispose
                if (root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String)
                    error = e.GetString();
            }
            catch { /* non-JSON / empty body */ }
            return new Envelope(resp.IsSuccessStatusCode, data, error);
        }

        private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        private static string GetString(JsonElement el, string name) =>
            el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";

        private static int GetInt(JsonElement el, string name) =>
            el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n) ? n : 0;

        private static bool GetBool(JsonElement el, string name) =>
            el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;
    }
}
