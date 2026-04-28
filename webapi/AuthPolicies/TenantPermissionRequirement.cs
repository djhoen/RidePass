using Microsoft.AspNetCore.Authorization;

namespace webapi.AuthPolicies
{
    public class TenantPermissionRequirement : IAuthorizationRequirement
    {
        public string Permission { get; }

        public TenantPermissionRequirement(string permission)
        {
            Permission = permission;
        }

        public static string PolicyName(string permission) => $"TenantPerm:{permission}";
    }
}
