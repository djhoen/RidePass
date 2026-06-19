using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Services.Embed;
using Services.Repositories.Data.PlatformData;
using Services.Repositories.Interfaces;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    /// <summary>
    /// Embed-widget framing policy. The single action here is hit internally by nginx
    /// (auth_request) for every /embed/* document so it can stamp a per-tenant
    /// Content-Security-Policy: frame-ancestors header. Anonymous: nginx calls it with
    /// the tenant subdomain as Host, the middleware resolves the tenant, and we return
    /// the computed frame-ancestors value in a response header for nginx to copy.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class EmbedController : ControllerBase
    {
        private readonly ITenantContext _tenantContext;
        private readonly IPlatformSettingRepository _settings;
        private readonly IMemoryCache _cache;

        public EmbedController(ITenantContext tenantContext, IPlatformSettingRepository settings, IMemoryCache cache)
        {
            _tenantContext = tenantContext;
            _settings = settings;
            _cache = cache;
        }

        // Short cache so the per-/embed-request subrequest doesn't hit the DB every time;
        // invalidated by the super-admin Misc settings save.
        internal const string GlobalOriginsCacheKey = "embed:global_origins";

        private async Task<List<string>> GlobalOrigins()
        {
            return (await _cache.GetOrCreateAsync(GlobalOriginsCacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);
                var raw = await _settings.Get(PlatformSettingKeys.EmbedGlobalAllowedOrigins);
                return EmbedPolicy.NormalizeList(EmbedPolicy.ParseOrigins(raw));
            }))!;
        }

        [HttpGet("FrameAncestors")]
        public async Task<IActionResult> FrameAncestors()
        {
            string value;
            if (!_tenantContext.IsResolved)
            {
                // Unknown host: block all framing.
                value = "'none'";
            }
            else
            {
                var tenant = _tenantContext.Tenant;
                value = EmbedPolicy.BuildFrameAncestors(
                    await GlobalOrigins(), tenant.EmbedAllowedOrigins, tenant.EmbedEnabled);
            }

            // nginx copies this onto the public response as the CSP frame-ancestors directive.
            Response.Headers["X-Embed-Frame-Ancestors"] = value;
            return NoContent();
        }
    }
}
