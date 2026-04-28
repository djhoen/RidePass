using Microsoft.AspNetCore.Authorization;

namespace webapi.AuthPolicies
{
    public class SuperAdminRequirement : IAuthorizationRequirement
    {
        public const string PolicyName = "SuperAdmin";
    }

    public class SuperAdminHandler : AuthorizationHandler<SuperAdminRequirement>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, SuperAdminRequirement requirement)
        {
            if (context.User.FindFirst("role")?.Value == "super_admin")
            {
                context.Succeed(requirement);
            }
            return Task.CompletedTask;
        }
    }
}
