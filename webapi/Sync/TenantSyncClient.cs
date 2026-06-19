namespace webapi.Sync
{
    /// <summary>
    /// Prod-side HTTP client that pulls from staging's TenantSync endpoints, presenting the
    /// shared key. Configured by TenantSync:SourceBaseUrl (the stage origin) + TenantSync:Key.
    /// </summary>
    public class TenantSyncClient
    {
        private readonly HttpClient _http;
        private readonly IConfiguration _config;

        public TenantSyncClient(HttpClient http, IConfiguration config)
        {
            _http = http;
            _config = config;
        }

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(_config["TenantSync:SourceBaseUrl"]) &&
            !string.IsNullOrWhiteSpace(_config["TenantSync:Key"]);

        /// <summary>Raw JSON of staging's unpublished-tenants list (ApiResponses-wrapped).</summary>
        public async Task<string> ListTenantsJson(CancellationToken ct)
        {
            using var resp = await Send(HttpMethod.Get, "api/TenantSync/Tenants", ct);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsStringAsync(ct);
        }

        /// <summary>Download a tenant's promotion bundle (zip bytes) from staging.</summary>
        public async Task<byte[]> DownloadBundle(Guid tenantId, CancellationToken ct)
        {
            using var resp = await Send(HttpMethod.Get, $"api/TenantSync/Export/{tenantId}", ct);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsByteArrayAsync(ct);
        }

        private Task<HttpResponseMessage> Send(HttpMethod method, string path, CancellationToken ct)
        {
            var baseUrl = _config["TenantSync:SourceBaseUrl"]?.TrimEnd('/');
            var key = _config["TenantSync:Key"];
            if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException("TenantSync:SourceBaseUrl / TenantSync:Key are not configured.");
            }
            var req = new HttpRequestMessage(method, $"{baseUrl}/{path}");
            req.Headers.Add("X-Tenant-Sync-Key", key);
            return _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        }
    }
}
