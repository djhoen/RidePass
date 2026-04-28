using Microsoft.AspNetCore.Authorization;
using webapi.Multitenancy;

namespace webapi.AuthPolicies
{
    public class TenantPermissionHandler : AuthorizationHandler<TenantPermissionRequirement>
    {
        private readonly ITenantContext _tenantContext;

        public TenantPermissionHandler(ITenantContext tenantContext)
        {
            _tenantContext = tenantContext;
        }

        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, TenantPermissionRequirement requirement)
        {
            var role = context.User.FindFirst("role")?.Value;
            if (string.IsNullOrEmpty(role))
            {
                return Task.CompletedTask;
            }

            // Super admins always pass — they can act as any tenant admin during support work.
            if (role == "super_admin")
            {
                context.Succeed(requirement);
                return Task.CompletedTask;
            }

            if (!TenantPermissions.ForRole(role).Contains(requirement.Permission))
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

            context.Succeed(requirement);
            return Task.CompletedTask;
        }
    }
}
