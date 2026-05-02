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
    /// </summary>
    public class HttpContextAuditLogger : IAuditLogger
    {
        private readonly IAuditLogRepository _repo;
        private readonly IUserRepository _users;
        private readonly IHttpContextAccessor _http;

        public HttpContextAuditLogger(IAuditLogRepository repo, IUserRepository users, IHttpContextAccessor http)
        {
            _repo = repo;
            _users = users;
            _http = http;
        }

        public async Task Log(string action, string summary,
            string? targetKind = null, Guid? targetId = null, Guid? tenantId = null, object? metadata = null)
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
                    actorId = uid;
                    var u = await _users.GetById(uid);
                    actorEmail = u?.Email;
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
    }
}
