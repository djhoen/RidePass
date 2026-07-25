using Services.Repositories.Interfaces;
using webapi.Helpers;

namespace webapi.Middleware
{
    /// <summary>
    /// Slides authenticated sessions. Any request carrying a still-valid token older than
    /// <see cref="RefreshAfter"/> gets a fresh token (same lifetime, same impersonation
    /// context) in the X-Refreshed-Token response header; the SPA swaps it in silently.
    /// An active user is therefore never logged out mid-use; only being idle past the
    /// token's lifetime (Jwt:IdleTimeoutMinutes for normal sessions) ends the session.
    /// Tokens minted before this feature carry no session_minutes claim and are left to
    /// expire on their original fixed schedule.
    /// </summary>
    public class SlidingSessionMiddleware
    {
        public const string HeaderName = "X-Refreshed-Token";

        // During impersonation the super admin's own (stashed) session must not expire
        // either, so their token is re-issued alongside and carried in this header.
        public const string OriginalHeaderName = "X-Refreshed-Original-Token";

        // Don't mint a token on every request from a busy screen; once the current one
        // is a couple minutes old is plenty to keep the window sliding.
        private static readonly TimeSpan RefreshAfter = TimeSpan.FromMinutes(2);

        private readonly RequestDelegate _next;

        public SlidingSessionMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            var (refreshed, refreshedOriginal) = await TryIssueRefreshedTokens(context);
            if (refreshed is not null)
            {
                context.Response.Headers[HeaderName] = refreshed;
            }
            if (refreshedOriginal is not null)
            {
                context.Response.Headers[OriginalHeaderName] = refreshedOriginal;
            }
            await _next(context);
        }

        private static async Task<(string? Refreshed, string? RefreshedOriginal)> TryIssueRefreshedTokens(HttpContext context)
        {
            var principal = context.User;
            if (principal.Identity?.IsAuthenticated != true) return (null, null);

            // Legacy fixed-life token (pre-sliding): leave it alone.
            if (!int.TryParse(principal.FindFirst("session_minutes")?.Value, out var sessionMinutes) || sessionMinutes <= 0)
                return (null, null);
            if (!long.TryParse(principal.FindFirst("exp")?.Value, out var exp)) return (null, null);
            if (!Guid.TryParse(principal.FindFirst("UserId")?.Value, out var userId)) return (null, null);

            var issuedAt = DateTimeOffset.FromUnixTimeSeconds(exp) - TimeSpan.FromMinutes(sessionMinutes);
            if (DateTimeOffset.UtcNow - issuedAt < RefreshAfter) return (null, null);

            // Re-read the user so a deactivation ends the slide and role changes take
            // effect at the next refresh instead of persisting for the token's life.
            var users = context.RequestServices.GetRequiredService<IUserRepository>();
            var user = await users.GetById(userId);
            if (user is null || user.Status != "active") return (null, null);

            Guid? impersonatedBy =
                Guid.TryParse(principal.FindFirst("impersonated_by")?.Value, out var imp) ? imp : null;

            var jwtIssuer = context.RequestServices.GetRequiredService<IJwtIssuer>();
            var refreshed = jwtIssuer.IssueForUser(user, TimeSpan.FromMinutes(sessionMinutes), impersonatedBy);

            // Impersonation: also slide the impersonating super admin's own session so
            // "stop impersonation" restores a live token however long the visit lasted.
            // The impersonated_by claim was minted server-side for a verified super
            // admin, but re-check the account is still an active super admin before
            // extending it. Default (idle-timeout) lifetime; post-restore sliding
            // takes over from there.
            string? refreshedOriginal = null;
            if (impersonatedBy.HasValue)
            {
                var original = await users.GetById(impersonatedBy.Value);
                if (original is not null && original.Status == "active" && original.Role == "super_admin")
                {
                    refreshedOriginal = jwtIssuer.IssueForUser(original);
                }
            }

            return (refreshed, refreshedOriginal);
        }
    }
}
