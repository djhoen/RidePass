namespace webapi.Controllers.API.Data.User
{
    // Bootstrap payload for an authenticated client (notably the operator app): identity
    // plus the SERVER-computed permission set, so the client renders capabilities without
    // re-implementing the role->permission map. TenantId is the staffer's own tenant
    // (null for global accounts / super admins).
    public class MeResponse
    {
        public Guid UserId { get; set; }
        public Guid? TenantId { get; set; }
        public string Email { get; set; } = null!;
        public string FirstName { get; set; } = null!;
        public string LastName { get; set; } = null!;
        public string Role { get; set; } = null!;
        public string[] Roles { get; set; } = System.Array.Empty<string>();
        public string[] Permissions { get; set; } = System.Array.Empty<string>();
        // Tenant gate setting the operator app needs: when true, staff must verify a photo ID at
        // check-in. False when no tenant is resolved.
        public bool RequireIdAtCheckin { get; set; }
    }
}
