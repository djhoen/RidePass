using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Data.AuditData;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Activity;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    // Tenant Admin -> Staff Activity. Reads back what the audit log recorded: who did what, when,
    // and from which address.
    //
    // Two audiences, deliberately split:
    //   * The whole tenant's activity, gated by audit.view (admin only). Oversight.
    //   * The caller's OWN activity, gated by nothing beyond being signed in. Everyone can see
    //     what is recorded about them, which is both fair and the point: a log people know exists
    //     and can look at deters far more than one discovered during an investigation.
    //
    // Every read goes through IAuditLogRepository.ListForTenant, whose tenantId is non-nullable,
    // so no code path here can widen to another tenant's rows.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class StaffActivityController : ControllerBase
    {
        // A tenant screen paging back further than this wants a report, not a log tail.
        private const int MaxTake = 500;
        private const int DefaultTake = 200;

        private readonly IAuditLogRepository _audit;
        private readonly ITenantContext _tenantContext;

        public StaffActivityController(IAuditLogRepository audit, ITenantContext tenantContext)
        {
            _audit = audit;
            _tenantContext = tenantContext;
        }

        private Guid? UserId => Guid.TryParse(User.FindFirst("UserId")?.Value, out var id) ? id : null;

        private static StaffActivityItem Map(AuditLogEntry e) => new()
        {
            Id = e.Id,
            ActorUserId = e.ActorUserId,
            ActorEmail = e.ActorEmail,
            ActorRole = e.ActorRole,
            Action = e.Action,
            Summary = e.Summary,
            TargetKind = e.TargetKind,
            TargetId = e.TargetId,
            IpAddress = e.IpAddress,
            Metadata = e.Metadata,
            CreatedAtUtc = DateTime.SpecifyKind(e.CreatedAt, DateTimeKind.Utc),
        };

        /// <summary>Everything recorded for this tenant, newest first. Optional filters narrow to
        /// one action type, one staff member, or a date window.</summary>
        [Authorize(Policy = TenantPermissions.Policy.AuditView)]
        [HttpGet]
        public async Task<IActionResult> List(
            [FromQuery] string? action,
            [FromQuery] Guid? actorUserId,
            [FromQuery] DateTime? fromUtc,
            [FromQuery] DateTime? toUtc,
            [FromQuery] int take = DefaultTake)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");

            var rows = await _audit.ListForTenant(
                _tenantContext.TenantId,
                string.IsNullOrWhiteSpace(action) ? null : action.Trim(),
                actorUserId,
                fromUtc,
                toUtc,
                Math.Clamp(take, 1, MaxTake));

            return new ApiResponses().OkResult(rows.Select(Map));
        }

        /// <summary>The caller's own activity. No permission beyond being signed in: this is the
        /// staffer's own record, and it is scoped to their user id as well as the tenant.</summary>
        [HttpGet("Mine")]
        public async Task<IActionResult> Mine(
            [FromQuery] DateTime? fromUtc,
            [FromQuery] DateTime? toUtc,
            [FromQuery] int take = DefaultTake)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (UserId is not Guid me) return new ApiResponses().BadRequestResult("Not signed in.");

            var rows = await _audit.ListForTenant(
                _tenantContext.TenantId,
                action: null,
                actorUserId: me,
                fromUtc,
                toUtc,
                Math.Clamp(take, 1, MaxTake));

            return new ApiResponses().OkResult(rows.Select(Map));
        }

        /// <summary>The distinct action names present for this tenant, so the filter dropdown
        /// offers what actually happened here instead of a hardcoded list that drifts as new
        /// audited actions are added.</summary>
        [Authorize(Policy = TenantPermissions.Policy.AuditView)]
        [HttpGet("Actions")]
        public async Task<IActionResult> Actions()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var rows = await _audit.ListForTenant(_tenantContext.TenantId, take: MaxTake);
            return new ApiResponses().OkResult(rows.Select(r => r.Action).Distinct().OrderBy(a => a));
        }
    }
}
