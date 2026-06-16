using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.User
{
    public class TenantUserListItem
    {
        public Guid Id { get; set; }
        public string Email { get; set; } = null!;
        public string FirstName { get; set; } = null!;
        public string LastName { get; set; } = null!;
        public string Role { get; set; } = null!;            // primary (highest-privilege)
        public string[] Roles { get; set; } = System.Array.Empty<string>();  // full set
        public string Status { get; set; } = null!;
        public DateTime CreatedAtUtc { get; set; }
    }

    public class CreateTenantUserRequest
    {
        [Required, EmailAddress] public string Email { get; set; } = null!;
        [Required] public string FirstName { get; set; } = null!;
        [Required] public string LastName { get; set; } = null!;
        // One or more roles. Role is kept for backward compatibility; if Roles is non-empty
        // it wins. The controller derives the primary from the resulting set.
        public string? Role { get; set; }
        public string[] Roles { get; set; } = System.Array.Empty<string>();
    }

    public class CreateTenantUserResponse
    {
        public Guid Id { get; set; }
        public string Email { get; set; } = null!;
        public string Role { get; set; } = null!;
        public string[] Roles { get; set; } = System.Array.Empty<string>();
        public string TemporaryPassword { get; set; } = null!;
    }

    public class UpdateTenantUserRoleRequest
    {
        public string? Role { get; set; }
        public string[] Roles { get; set; } = System.Array.Empty<string>();
    }

    public class UpdateTenantUserStatusRequest
    {
        [Required] public string Status { get; set; } = null!;  // "active" | "disabled"
    }

    public class ResetTenantUserPasswordResponse
    {
        public string TemporaryPassword { get; set; } = null!;
    }
}
