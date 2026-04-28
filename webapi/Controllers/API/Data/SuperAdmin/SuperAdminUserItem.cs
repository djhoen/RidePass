namespace webapi.Controllers.API.Data.SuperAdmin
{
    public class SuperAdminUserItem
    {
        public Guid Id { get; set; }
        public Guid? TenantId { get; set; }
        public string? TenantSubdomain { get; set; }
        public string Email { get; set; } = null!;
        public string FirstName { get; set; } = null!;
        public string LastName { get; set; } = null!;
        public string Role { get; set; } = null!;
        public string Status { get; set; } = null!;
    }
}
