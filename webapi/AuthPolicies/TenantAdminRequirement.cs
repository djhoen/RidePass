using Microsoft.AspNetCore.Authorization;

namespace webapi.AuthPolicies
{
    public class TenantAdminRequirement : IAuthorizationRequirement
    {
        public const string PolicyName = "TenantAdmin";
    }
}
