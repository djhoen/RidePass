namespace webapi.Controllers.API.Data.SuperAdmin
{
    public class ImpersonationResponse
    {
        public string Token { get; set; } = null!;
        public Guid UserId { get; set; }
        public string Email { get; set; } = null!;
        public string FirstName { get; set; } = null!;
        public string LastName { get; set; } = null!;
        public string Role { get; set; } = null!;
        public Guid? TenantId { get; set; }
        public string? TenantSubdomain { get; set; }
    }
}
