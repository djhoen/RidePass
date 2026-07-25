using Microsoft.AspNetCore.Authorization;

namespace webapi.AuthPolicies
{
    /// <summary>
    /// Requires the caller to hold a tenant permission. A requirement may name SEVERAL permissions,
    /// in which case holding ANY ONE of them satisfies it.
    ///
    /// The "any" form exists because ASP.NET combines attribute policies with AND: a class-level
    /// [Authorize(Policy = X)] can never be relaxed by an action-level attribute, only tightened.
    /// So an endpoint two different job roles must both reach (a catalog read that both the
    /// catalog manager and the shop cashier need) cannot be expressed as two separate policies. It
    /// has to be one policy that accepts either permission.
    /// </summary>
    public class TenantPermissionRequirement : IAuthorizationRequirement
    {
        /// <summary>Holding any one of these satisfies the requirement.</summary>
        public IReadOnlyList<string> Permissions { get; }

        public TenantPermissionRequirement(string permission)
            : this(new[] { permission }) { }

        public TenantPermissionRequirement(params string[] permissions)
        {
            if (permissions is null || permissions.Length == 0)
                throw new ArgumentException("A permission requirement must name at least one permission.", nameof(permissions));
            Permissions = permissions;
        }

        public static string PolicyName(string permission) => $"TenantPerm:{permission}";

        /// <summary>Policy name for an any-of requirement. Order matters only for the name, so
        /// callers must use the same order the policy was registered with; the
        /// TenantPermissions.Policy constants are the single source of both.</summary>
        public static string AnyPolicyName(params string[] permissions) =>
            $"TenantPermAny:{string.Join('|', permissions)}";
    }
}
