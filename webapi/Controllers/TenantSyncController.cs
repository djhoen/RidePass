using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Interfaces;
using Services.TenantSync;
using webapi.Sync;

namespace webapi.Controllers
{
    /// <summary>
    /// The SOURCE side of stage->prod tenant promotion. Lives on staging (and exists on prod
    /// too, but only answers where TenantSync:Key is set). Machine-authenticated via
    /// TenantSyncAuth (shared key + prod IP allowlist) — NO super-admin JWT, because the
    /// caller is the prod server, not a browser. Read-only: it never mutates staging.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [TenantSyncAuth]
    public class TenantSyncController : ControllerBase
    {
        private readonly ITenantSyncRepository _sync;
        private readonly ITenantRepository _tenants;
        private readonly TenantSyncImageStore _images;

        public TenantSyncController(
            ITenantSyncRepository sync,
            ITenantRepository tenants,
            TenantSyncImageStore images)
        {
            _sync = sync;
            _tenants = tenants;
            _images = images;
        }

        /// <summary>Unpublished tenants on this (staging) environment, available to promote.</summary>
        [HttpGet("Tenants")]
        public async Task<IActionResult> ListUnpublished()
        {
            var all = await _tenants.ListAll();
            var items = all
                .Where(t => !t.IsPublished)
                .OrderBy(t => t.Subdomain, StringComparer.OrdinalIgnoreCase)
                .Select(t => new
                {
                    id = t.Id,
                    subdomain = t.Subdomain,
                    displayName = t.DisplayName,
                    everPublished = t.FirstPublishedAt != null,
                });
            return new ApiResponses().OkResult(items);
        }

        /// <summary>
        /// Stream a tenant's promotion bundle as a zip: meta.json (schema version + tenant id/
        /// subdomain + this env's image public base) + manifest.json (the whitelisted config
        /// rows as type-faithful JSON) + uploads/ (the tenant's image bytes from this bucket).
        /// Source must be unpublished.
        /// </summary>
        [HttpGet("Export/{id:guid}")]
        public async Task<IActionResult> Export(Guid id, CancellationToken ct)
        {
            var tenant = await _tenants.GetById(id);
            if (tenant is null) return new ApiResponses().NotFoundResult("Tenant not found.");
            if (tenant.IsPublished) return new ApiResponses().BadRequestResult("Only unpublished tenants can be exported.");

            var tables = await _sync.ExportTables(id);
            var schema = await _sync.GetLatestSchemaVersion();
            var images = await _images.ReadTenantImages(id, ct);

            var tablesNode = new JsonObject();
            foreach (var kv in tables)
            {
                tablesNode[kv.Key] = JsonNode.Parse(kv.Value);
            }
            var manifest = new JsonObject { ["tables"] = tablesNode };

            var meta = new JsonObject
            {
                ["schemaVersion"] = schema,
                ["tenantId"] = tenant.Id.ToString(),
                ["subdomain"] = tenant.Subdomain,
                ["displayName"] = tenant.DisplayName,
                ["imagePublicBase"] = _images.PublicBaseUrl,
                ["exportedAtUtc"] = DateTime.UtcNow.ToString("o"),
            };

            using var mem = new MemoryStream();
            using (var zip = new ZipArchive(mem, ZipArchiveMode.Create, leaveOpen: true))
            {
                await WriteEntry(zip, "meta.json", Encoding.UTF8.GetBytes(meta.ToJsonString()));
                await WriteEntry(zip, "manifest.json", Encoding.UTF8.GetBytes(manifest.ToJsonString()));
                foreach (var img in images)
                {
                    await WriteEntry(zip, img.Key, img.Bytes);
                }
            }
            return File(mem.ToArray(), "application/zip", $"tenant-{tenant.Subdomain}.zip");
        }

        private static async Task WriteEntry(ZipArchive zip, string name, byte[] bytes)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            await using var s = entry.Open();
            await s.WriteAsync(bytes);
        }
    }
}
