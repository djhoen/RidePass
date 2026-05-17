namespace webapi.Controllers.API.Data.SuperAdmin
{
    public class CreateTenantResponse
    {
        public Guid TenantId { get; set; }
        public string Subdomain { get; set; } = null!;
        public string DisplayName { get; set; } = null!;
        public string TenantType { get; set; } = null!;
        public string Timezone { get; set; } = null!;

        // Present only if an initial tenant_admin was provisioned. This is the only time
        // the one-time password is ever exposed — the super admin must capture it now.
        public Guid? AdminUserId { get; set; }
        public string? AdminEmail { get; set; }
        public string? AdminTemporaryPassword { get; set; }
    }
}
