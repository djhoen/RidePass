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

        public TenantResolutionMiddleware(RequestDelegate next, IConfiguration configuration)
        {
            _next = next;
            _rootDomain = (configuration["Tenant:RootDomain"]
                ?? throw new InvalidOperationException("Tenant:RootDomain is not configured."))
                .ToLowerInvariant();
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

            // In Development, allow the SPA to tell us the tenant via X-Tenant-Subdomain
            // so browser origin (acme.ridepass.local:3000) and API origin (localhost:5070) can differ.
            if (subdomain is null && env.IsDevelopment())
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

            tenantContext.SetTenant(tenant);
            context.Items["TenantId"] = tenant.Id;

            await _next(context);
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
