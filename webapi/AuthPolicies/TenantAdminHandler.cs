using Microsoft.AspNetCore.Authorization;
using webapi.Multitenancy;

namespace webapi.AuthPolicies
{
    public class TenantAdminHandler : AuthorizationHandler<TenantAdminRequirement>
    {
        private readonly ITenantContext _tenantContext;

        public TenantAdminHandler(ITenantContext tenantContext)
        {
            _tenantContext = tenantContext;
        }

        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, TenantAdminRequirement requirement)
        {
            var role = context.User.FindFirst("role")?.Value;

            if (role == "super_admin")
            {
                context.Succeed(requirement);
                return Task.CompletedTask;
            }

            if (role != "tenant_admin")
            {
                return Task.CompletedTask;
            }

            var tenantClaim = context.User.FindFirst("tenant_id")?.Value;
            if (!Guid.TryParse(tenantClaim, out var claimTenantId))
            {
                return Task.CompletedTask;
            }

            if (_tenantContext.IsResolved && _tenantContext.TenantId == claimTenantId)
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }
}
