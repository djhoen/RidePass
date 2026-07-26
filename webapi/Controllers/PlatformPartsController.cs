using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Audit;
using Services.Helpers;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;

namespace webapi.Controllers
{
    /// <summary>
    /// Operator controls for the shared parts library. Super admin only, and deliberately a
    /// separate controller from the per-tenant bike shop: this is the one bike shop table that
    /// spans tenants, so the endpoints that administer it should not sit behind a tenant policy
    /// where a track admin could ever reach them.
    ///
    /// There is no "browse the library" endpoint here on purpose. Shops read it one barcode at a
    /// time through the register's scan resolver; a bulk export would turn a lookup aid into a
    /// redistributable database, which is the thing the vendor terms actually care about.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = SuperAdminRequirement.PolicyName)]
    public class PlatformPartsController : ControllerBase
    {
        private readonly IPlatformPartRepository _parts;
        private readonly IAuditLogger _audit;

        public PlatformPartsController(IPlatformPartRepository parts, IAuditLogger audit)
        {
            _parts = parts;
            _audit = audit;
        }

        /// <summary>What the library is made of, by source. 'tenant_confirmed' rows are RidePass's
        /// own data; anything else is a vendor slug and is subject to that vendor's terms.</summary>
        [HttpGet("Stats")]
        public async Task<IActionResult> Stats()
        {
            var counts = await _parts.CountsBySource();
            return new ApiResponses().OkResult(new
            {
                bySource = counts.Select(c => new { source = c.Source, partCount = c.PartCount }),
                totalParts = counts.Sum(c => c.PartCount),
            });
        }

        /// <summary>
        /// The licensing kill switch: drop every entry cached from one external vendor. Go-UPC's
        /// terms require deleting product data on termination, so this has to exist before a vendor
        /// is ever switched on rather than be written under time pressure afterwards.
        ///
        /// Refuses to touch 'tenant_confirmed' and 'staff'. Those are not any vendor's data, they
        /// are the library's whole point, and a fat-fingered slug should not be able to erase them.
        /// </summary>
        [HttpDelete("Source/{source}")]
        public async Task<IActionResult> PurgeSource(string source)
        {
            var slug = source?.Trim().ToLowerInvariant() ?? string.Empty;
            if (slug.Length == 0) return new ApiResponses().BadRequestResult("Name the source to purge.");
            if (slug is "tenant_confirmed" or "staff")
            {
                return new ApiResponses().BadRequestResult(
                    "That source is RidePass's own data, not a vendor's, so it can't be purged here.");
            }

            var removed = await _parts.PurgeSource(slug);
            await _audit.Log("platform_parts.purge_source",
                $"Purged {removed} shared parts library entries sourced from '{slug}'", "platform_part", null);
            return new ApiResponses().OkResult(new { source = slug, removed });
        }
    }
}
