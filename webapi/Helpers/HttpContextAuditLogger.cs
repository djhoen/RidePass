using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Services.Audit;
using Services.Repositories.Data.AuditData;
using Services.Repositories.Interfaces;

namespace webapi.Helpers
{
    /// <summary>
    /// AuditLogger impl that reads the actor (UserId, role, email) and IP from the current request's
    /// HttpContext. Lives in webapi/ because IHttpContextAccessor is an ASP.NET concept; the Services
    /// project stays platform-neutral.
    ///
    /// Audit writes are best-effort by design: Log is always called AFTER the audited action has
    /// committed, so throwing here would fail a request whose primary effect already happened
    /// (e.g. "create super admin" 500s but the user exists, and a retry says "already exists").
    /// A failed write is logged loudly instead of failing the caller.
    /// </summary>
    public class HttpContextAuditLogger : IAuditLogger
    {
        private readonly IAuditLogRepository _repo;
        private readonly IUserRepository _users;
        private readonly IHttpContextAccessor _http;
        private readonly ILogger<HttpContextAuditLogger> _logger;

        public HttpContextAuditLogger(IAuditLogRepository repo, IUserRepository users, IHttpContextAccessor http,
            ILogger<HttpContextAuditLogger> logger)
        {
            _repo = repo;
            _users = users;
            _http = http;
            _logger = logger;
        }

        public async Task Log(string action, string summary,
            string? targetKind = null, Guid? targetId = null, Guid? tenantId = null, object? metadata = null)
        {
            try
            {
                var ctx = _http.HttpContext;
                Guid? actorId = null;
                string? actorRole = null;
                string? actorEmail = null;
                string? ip = null;

                if (ctx?.User is { } user)
                {
                    actorRole = user.FindFirst("role")?.Value;
                    if (Guid.TryParse(user.FindFirst("UserId")?.Value, out var uid))
                    {
                        // Only record the actor id when the row still exists: audit_log.actor_user_id
                        // has an FK to users, and a stale-but-valid JWT (e.g. issued before a
                        // stage copy-down recreated the users table) would otherwise fail the insert.
                        var u = await _users.GetById(uid);
                        if (u is not null)
                        {
                            actorId = uid;
                            actorEmail = u.Email;
                        }
                        else
                        {
                            _logger.LogWarning(
                                "Audit actor {UserId} has no users row; writing {Action} with a null actor.",
                                uid, action);
                        }
                    }
                    ip = ctx.Connection.RemoteIpAddress?.ToString();
                }

                var metadataJson = metadata is null ? null : JsonSerializer.Serialize(metadata);

                await _repo.Insert(new AuditLogEntry
                {
                    ActorUserId = actorId,
                    ActorEmail = actorEmail,
                    ActorRole = actorRole,
                    Action = action,
                    TargetKind = targetKind,
                    TargetId = targetId,
                    Summary = summary,
                    Metadata = metadataJson,
                    IpAddress = ip,
                    TenantId = tenantId,
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Audit write failed for {Action}: {Summary}", action, summary);
            }
        }
    }
}
