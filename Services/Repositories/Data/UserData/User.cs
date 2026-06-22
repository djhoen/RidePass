namespace Services.Repositories.Data.UserData
{
    public class User
    {
        public Guid Id { get; set; }
        public Guid? TenantId { get; set; }
        public string Email { get; set; } = null!;
        public string PasswordHash { get; set; } = null!;
        public string FirstName { get; set; } = null!;
        public string LastName { get; set; } = null!;
        public string Role { get; set; } = null!;        // primary role (scope/identity/display)
        public string[] Roles { get; set; } = System.Array.Empty<string>();  // full set; permissions = union
        public string Status { get; set; } = null!;
        public string? Phone { get; set; }
        public DateTime? Birthdate { get; set; }
        public string? EmergencyContactName { get; set; }
        public string? EmergencyContactPhone { get; set; }
        public string? AddressLine { get; set; }
        public string? AddressLine2 { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? PostalCode { get; set; }
        public string? Country { get; set; }
        public string? Bike { get; set; }
        public string? RaceNumber { get; set; }
        public string? ImageUrl { get; set; }
        public bool EmailVerified { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
