using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.User
{
    public class TenantUserListItem
    {
        public Guid Id { get; set; }
        public string Email { get; set; } = null!;
        public string FirstName { get; set; } = null!;
        public string LastName { get; set; } = null!;
        public string Role { get; set; } = null!;
        public string Status { get; set; } = null!;
        public DateTime CreatedAtUtc { get; set; }
    }

    public class CreateTenantUserRequest
    {
        [Required, EmailAddress] public string Email { get; set; } = null!;
        [Required] public string FirstName { get; set; } = null!;
        [Required] public string LastName { get; set; } = null!;
        [Required] public string Role { get; set; } = null!;
    }

    public class CreateTenantUserResponse
    {
        public Guid Id { get; set; }
        public string Email { get; set; } = null!;
        public string Role { get; set; } = null!;
        public string TemporaryPassword { get; set; } = null!;
    }

    public class UpdateTenantUserRoleRequest
    {
        [Required] public string Role { get; set; } = null!;
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
