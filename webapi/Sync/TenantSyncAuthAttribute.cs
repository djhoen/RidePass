using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace webapi.Sync
{
    /// <summary>
    /// Machine-to-machine auth for the stage->prod tenant promotion endpoints. There is NO
    /// JWT here (JWTs are signed per-environment and can't cross), so prod authenticates to
    /// stage with a shared secret. A request is accepted only when BOTH hold:
    ///   1. the X-Tenant-Sync-Key header equals TenantSync:Key (constant-time compare), and
    ///   2. the caller's IP is in TenantSync:AllowedIps (the prod droplet).
    /// If TenantSync:Key is unset the whole sync surface is dark (404) — so an environment
    /// that hasn't opted in exposes nothing.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class TenantSyncAuthAttribute : Attribute, IAsyncActionFilter
    {
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var config = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();

            var expectedKey = config["TenantSync:Key"];
            if (string.IsNullOrWhiteSpace(expectedKey))
            {
                // Sync not enabled in this environment — don't even admit the endpoint exists.
                context.Result = new ObjectResult(new { error = "Not found." }) { StatusCode = 404 };
                return;
            }

            var presented = context.HttpContext.Request.Headers["X-Tenant-Sync-Key"].ToString();
            if (string.IsNullOrEmpty(presented) || !FixedTimeEquals(presented, expectedKey))
            {
                context.Result = new ObjectResult(new { error = "Invalid sync key." }) { StatusCode = 401 };
                return;
            }

            var allowed = (config["TenantSync:AllowedIps"] ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (allowed.Length > 0)
            {
                var clientIp = ClientIp(context.HttpContext);
                if (clientIp is null || !allowed.Contains(clientIp))
                {
                    context.Result = new ObjectResult(new { error = "Caller IP not allowed." }) { StatusCode = 403 };
                    return;
                }
            }

            await next();
        }

        // The trustworthy hop is the LAST entry in X-Forwarded-For — the address nginx
        // actually observed and appended. Earlier entries are caller-supplied and can be
        // spoofed (a forged XFF naming the prod IP would otherwise bypass the allowlist).
        // Fall back to the socket peer for direct (un-proxied) calls.
        private static string? ClientIp(HttpContext ctx)
        {
            var xff = ctx.Request.Headers["X-Forwarded-For"].ToString();
            if (!string.IsNullOrEmpty(xff))
            {
                var parts = xff.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length > 0) return parts[^1];
            }
            return ctx.Connection.RemoteIpAddress?.ToString();
        }

        private static bool FixedTimeEquals(string a, string b)
            => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
    }
}
