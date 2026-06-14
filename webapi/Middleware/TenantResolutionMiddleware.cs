using Microsoft.Extensions.Caching.Memory;
using Services.Repositories.Data.TenantData;
using Services.Repositories.Interfaces;
using webapi.Multitenancy;

namespace webapi.Middleware
{
    public class TenantResolutionMiddleware
    {
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

        private readonly RequestDelegate _next;
        private readonly string _rootDomain;
        private readonly bool _allowApiClientTenantHeader;

        public TenantResolutionMiddleware(RequestDelegate next, IConfiguration configuration)
        {
            _next = next;
            _rootDomain = (configuration["Tenant:RootDomain"]
                ?? throw new InvalidOperationException("Tenant:RootDomain is not configured."))
                .ToLowerInvariant();
            // Native API clients (RidePassCashier mobile app) don't have a subdomain
            // to resolve from. When this flag is on, the X-Tenant-Subdomain header is
            // honored regardless of environment. Safe because TenantPermissionHandler
            // still cross-checks the JWT's tenant_id claim against the resolved tenant,
            // so a forged header doesn't help an attacker without a valid JWT for that
            // tenant. Default off — must be opted in via configuration.
            _allowApiClientTenantHeader = bool.TryParse(
                configuration["Tenant:AllowApiClientTenantHeader"], out var allow) && allow;
        }

        public async Task InvokeAsync(
            HttpContext context,
            ITenantRepository tenantRepository,
            TenantContext tenantContext,
            IMemoryCache cache,
            IWebHostEnvironment env)
        {
            var host = context.Request.Host.Host.ToLowerInvariant();
            var subdomain = ExtractSubdomain(host);

            // In Development, the SPA on a separate origin uses the header. In
            // any environment when the API-client flag is on, native apps
            // (mobile cashier) use the same mechanism.
            if (subdomain is null && (env.IsDevelopment() || _allowApiClientTenantHeader))
            {
                var header = context.Request.Headers["X-Tenant-Subdomain"].ToString();
                if (!string.IsNullOrWhiteSpace(header) && !header.Contains('.'))
                {
                    subdomain = header.ToLowerInvariant();
                }
            }

            if (subdomain is null)
            {
                // Apex or unknown host shape — no tenant, continue so platform/health routes can respond.
                await _next(context);
                return;
            }

            var cacheKey = $"tenant:{subdomain}";
            var tenant = await cache.GetOrCreateAsync(cacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheDuration;
                return await tenantRepository.GetBySubdomain(subdomain);
            });

            if (tenant is null || tenant.Status != "active")
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsync($"Unknown or inactive tenant: {subdomain}");
                return;
            }

            // Unpublished tenants are dark to the public — only the tenant's own
            // staff and super admins may reach the site (to set it up before
            // launch). Same 404 as inactive so we don't reveal it exists.
            if (!tenant.IsPublished && !MayAccessUnpublished(context, tenant.Id))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsync($"Unknown or inactive tenant: {subdomain}");
                return;
            }

            tenantContext.SetTenant(tenant);
            context.Items["TenantId"] = tenant.Id;

            await _next(context);
        }

        // A request may reach an unpublished tenant only if it's authenticated as
        // a super admin or as a user belonging to that tenant. Relies on
        // UseAuthentication running before this middleware (see Program.cs).
        private static bool MayAccessUnpublished(HttpContext context, Guid tenantId)
        {
            var user = context.User;
            if (user?.Identity is not { IsAuthenticated: true }) return false;
            if (user.FindFirst("role")?.Value == "super_admin") return true;
            return user.FindFirst("tenant_id")?.Value == tenantId.ToString();
        }

        private string? ExtractSubdomain(string host)
        {
            // host is already lowercased
            if (host == _rootDomain) return null;        // apex
            if (host == "localhost") return null;        // local apex
            if (System.Net.IPAddress.TryParse(host, out _)) return null; // IP literal

            var suffix = "." + _rootDomain;
            if (host.EndsWith(suffix))
            {
                var prefix = host.Substring(0, host.Length - suffix.Length);
                // only accept a single-level subdomain for now (e.g., "acme" but not "foo.acme")
                if (prefix.Length > 0 && !prefix.Contains('.'))
                {
                    return prefix;
                }
            }

            return null;
        }
    }
}
