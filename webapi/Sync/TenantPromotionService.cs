using System.IO.Compression;
using System.Text.Json.Nodes;
using Services.Repositories.Interfaces;
using Services.TenantSync;

namespace webapi.Sync
{
    public class PromotionResult
    {
        // preview | created | replaced | blocked
        public string Status { get; set; } = null!;
        public string Mode { get; set; } = null!;        // create | replace
        public string? Reason { get; set; }              // set when blocked
        public Guid TenantId { get; set; }
        public string Subdomain { get; set; } = null!;
        public string DisplayName { get; set; } = null!;
        public Dictionary<string, int> Counts { get; set; } = new();
    }

    /// <summary>
    /// Prod-side orchestration of a tenant promotion: pull the bundle from staging, decide
    /// create / replace / blocked, and (on confirm) run the guarded transactional import +
    /// copy images into the prod bucket.
    /// </summary>
    public class TenantPromotionService
    {
        private static readonly string[] NullKeys =
        {
            "first_published_at",
            "stripe_connect_account_id", "stripe_connect_status", "stripe_terminal_location_id",
            "twilio_subaccount_sid", "twilio_auth_token_encrypted", "twilio_from_number",
            "twilio_messaging_service_sid", "sms_enabled_at_utc",
            "loampass_mx_destination_id",
            "custom_domain", "embed_allowed_origins", "external_home_url", "external_events_url",
            "daily_status_open", "daily_status_message", "daily_status_updated_at",
        };

        private readonly TenantSyncClient _client;
        private readonly ITenantSyncRepository _sync;
        private readonly ITenantRepository _tenants;
        private readonly TenantSyncImageStore _images;

        public TenantPromotionService(
            TenantSyncClient client,
            ITenantSyncRepository sync,
            ITenantRepository tenants,
            TenantSyncImageStore images)
        {
            _client = client;
            _sync = sync;
            _tenants = tenants;
            _images = images;
        }

        public Task<string> ListStageTenantsJson(CancellationToken ct) => _client.ListTenantsJson(ct);

        public async Task<PromotionResult> Promote(Guid stageTenantId, bool confirm, CancellationToken ct)
        {
            var zip = await _client.DownloadBundle(stageTenantId, ct);
            var bundle = ParseBundle(zip);

            var result = new PromotionResult
            {
                TenantId = bundle.TenantId,
                Subdomain = bundle.Subdomain,
                DisplayName = bundle.DisplayName,
                Counts = bundle.HeadlineCounts(),
            };

            // 1. schema must match exactly.
            var prodSchema = await _sync.GetLatestSchemaVersion();
            if (!string.Equals(bundle.SchemaVersion, prodSchema, StringComparison.Ordinal))
            {
                return Block(result, $"Schema mismatch: stage is at '{bundle.SchemaVersion}', prod is at '{prodSchema}'. Deploy prod first, then retry.");
            }

            // 2. subdomain must not belong to a DIFFERENT tenant on prod.
            var subOwner = await _tenants.GetBySubdomain(bundle.Subdomain);
            if (subOwner is not null && subOwner.Id != bundle.TenantId)
            {
                return Block(result, $"Subdomain '{bundle.Subdomain}' already belongs to a different tenant on prod.");
            }

            // 3. create vs replace vs blocked.
            var existing = await _tenants.GetById(bundle.TenantId);
            if (existing is null)
            {
                result.Mode = "create";
            }
            else
            {
                if (existing.FirstPublishedAt is not null)
                {
                    return Block(result, "This tenant has been published on prod before, so it can never be overwritten.");
                }
                var liveOrders = await _sync.CountLiveOrders(existing.Id);
                if (liveOrders > 0)
                {
                    return Block(result, $"This tenant has {liveOrders} live order(s) on prod, so it can't be overwritten.");
                }
                result.Mode = "replace";
            }

            // 4. preview only.
            if (!confirm)
            {
                result.Status = "preview";
                return result;
            }

            // 5. process + import transactionally.
            var tables = ProcessTables(bundle);
            await _sync.ImportTables(bundle.TenantId, replace: result.Mode == "replace", tables);

            // 6. copy images into the prod bucket (post-commit; S3 isn't transactional).
            foreach (var img in bundle.Images)
            {
                await _images.PutObject(img, ct);
            }

            result.Status = result.Mode == "replace" ? "replaced" : "created";
            return result;
        }

        private Dictionary<string, string> ProcessTables(ParsedBundle bundle)
        {
            // Rewrite image URLs from the stage base to prod's, across the whole manifest text
            // (covers every column that stores a URL, no enumeration needed).
            var manifestText = bundle.ManifestText;
            if (!string.IsNullOrEmpty(bundle.ImagePublicBase)
                && !string.IsNullOrEmpty(_images.PublicBaseUrl)
                && !string.Equals(bundle.ImagePublicBase, _images.PublicBaseUrl, StringComparison.Ordinal))
            {
                manifestText = manifestText.Replace(bundle.ImagePublicBase, _images.PublicBaseUrl);
            }

            var tablesNode = JsonNode.Parse(manifestText)!.AsObject()["tables"]!.AsObject();
            var tables = new Dictionary<string, string>();
            foreach (var kv in tablesNode)
            {
                tables[kv.Key] = kv.Value!.ToJsonString();
            }

            if (tables.TryGetValue("tenant", out var tenantJson))
            {
                tables["tenant"] = ApplyTenantResets(tenantJson);
            }
            return tables;
        }

        // Null/reset the environment-specific columns; force unpublished; carry the
        // service-charge rate (decision: keep stage's commercial terms).
        private static string ApplyTenantResets(string tenantJsonArray)
        {
            var arr = JsonNode.Parse(tenantJsonArray)!.AsArray();
            if (arr.Count == 0) return tenantJsonArray;
            var t = arr[0]!.AsObject();
            foreach (var k in NullKeys) t[k] = null;
            t["is_published"] = false;
            t["custom_domain_verified"] = false;
            t["embed_enabled"] = false;
            t["sms_enabled"] = false;
            t["client_type"] = "hosted";
            t["embed_event_target"] = "external";
            return arr.ToJsonString();
        }

        private static PromotionResult Block(PromotionResult r, string reason)
        {
            r.Status = "blocked";
            r.Reason = reason;
            return r;
        }

        private static ParsedBundle ParseBundle(byte[] zipBytes)
        {
            using var ms = new MemoryStream(zipBytes);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

            string ReadText(string name)
            {
                var e = zip.GetEntry(name) ?? throw new InvalidOperationException($"Bundle is missing {name}.");
                using var s = e.Open();
                using var r = new StreamReader(s);
                return r.ReadToEnd();
            }

            var meta = JsonNode.Parse(ReadText("meta.json"))!.AsObject();
            var manifestText = ReadText("manifest.json");

            var images = new List<SyncImage>();
            foreach (var e in zip.Entries)
            {
                if (!e.FullName.StartsWith("uploads/", StringComparison.Ordinal) || e.FullName.EndsWith("/", StringComparison.Ordinal))
                    continue;
                using var s = e.Open();
                using var msi = new MemoryStream();
                s.CopyTo(msi);
                images.Add(new SyncImage
                {
                    Key = e.FullName,
                    Bytes = msi.ToArray(),
                    ContentType = ContentTypeFor(Path.GetExtension(e.FullName)),
                });
            }

            return new ParsedBundle
            {
                SchemaVersion = (string?)meta["schemaVersion"],
                TenantId = Guid.Parse((string)meta["tenantId"]!),
                Subdomain = (string)meta["subdomain"]!,
                DisplayName = (string)meta["displayName"]!,
                ImagePublicBase = (string?)meta["imagePublicBase"] ?? string.Empty,
                ManifestText = manifestText,
                Images = images,
            };
        }

        private static string ContentTypeFor(string ext) => ext.ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".ico" => "image/x-icon",
            ".gif" => "image/gif",
            _ => "application/octet-stream",
        };

        private class ParsedBundle
        {
            public string? SchemaVersion { get; set; }
            public Guid TenantId { get; set; }
            public string Subdomain { get; set; } = null!;
            public string DisplayName { get; set; } = null!;
            public string ImagePublicBase { get; set; } = string.Empty;
            public string ManifestText { get; set; } = null!;
            public List<SyncImage> Images { get; set; } = new();

            public Dictionary<string, int> HeadlineCounts()
            {
                var tables = JsonNode.Parse(ManifestText)!.AsObject()["tables"]!.AsObject();
                int Count(string t) => tables[t]?.AsArray()?.Count ?? 0;
                return new Dictionary<string, int>
                {
                    ["events"] = Count("event"),
                    ["ticketTiers"] = Count("event_ticket_tier"),
                    ["addOns"] = Count("event_extra_product"),
                    ["seasonPasses"] = Count("season_pass_product"),
                    ["images"] = Images.Count,
                };
            }
        }
    }
}
