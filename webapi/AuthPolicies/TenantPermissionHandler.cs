using Microsoft.AspNetCore.Authorization;
using Services.Access;
using webapi.Middleware;
using webapi.Multitenancy;

namespace webapi.AuthPolicies
{
    public class TenantPermissionHandler : AuthorizationHandler<TenantPermissionRequirement>
    {
        private readonly ITenantContext _tenantContext;
        private readonly IHttpContextAccessor _http;

        public TenantPermissionHandler(ITenantContext tenantContext, IHttpContextAccessor http)
        {
            _tenantContext = tenantContext;
            _http = http;
        }

        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, TenantPermissionRequirement requirement)
        {
            // A multi-role staffer carries one "role" claim per role; permissions are the union.
            var roles = context.User.FindAll("role").Select(c => c.Value).ToList();
            if (roles.Count == 0)
            {
                return Task.CompletedTask;
            }

            // Super admins always pass — they can act as any tenant admin during support work.
            if (roles.Contains("super_admin"))
            {
                context.Succeed(requirement);
                return Task.CompletedTask;
            }

            // Any ONE of the named permissions grants: a requirement usually names one, but a
            // shared endpoint (a catalog read both the catalog manager and the shop cashier need)
            // names several.
            var held = TenantPermissions.ForRoles(roles);
            if (!requirement.Permissions.Any(held.Contains))
            {
                return Task.CompletedTask;
            }

            // Tenant-scoped roles must be acting on their own tenant.
            var tenantClaim = context.User.FindFirst("tenant_id")?.Value;
            if (!Guid.TryParse(tenantClaim, out var claimTenantId))
            {
                return Task.CompletedTask;
            }
            if (!_tenantContext.IsResolved || _tenantContext.TenantId != claimTenantId)
            {
                return Task.CompletedTask;
            }

            // Where and when, on top of who (Script0239). Only applies to the money-moving tier,
            // and only to the permission actually being exercised: a staffer who holds both
            // sales.refund and reports.view can still read reports from home, because that
            // request comes in on a requirement this set does not contain.
            //
            // Checked last so it can only ever narrow an otherwise-granted request, never widen
            // one, and skipped entirely for super admins by the early return above.
            if (requirement.Permissions.Any(TenantPermissions.LocationRestrictable.Contains))
            {
                var denial = StaffAccessPolicy.Evaluate(
                    _tenantContext.Tenant,
                    _http.HttpContext?.Connection.RemoteIpAddress,
                    DateTime.UtcNow);
                if (denial != StaffAccessPolicy.Denial.None)
                {
                    // Leave a note for StaffAccessDenialMiddleware. An authorization handler can
                    // only succeed or stay silent, so without this the staffer gets a bare 403 and
                    // no idea why: during an event that becomes a phone call, not a shrug.
                    if (_http.HttpContext is { } ctx)
                    {
                        ctx.Items[StaffAccessDenialMiddleware.DenialItemKey] = denial;
                    }
                    return Task.CompletedTask;
                }
            }

            context.Succeed(requirement);
            return Task.CompletedTask;
        }
    }
}
