namespace webapi.Controllers.API.Data.SuperAdmin
{
    public class SuperAdminUpdateUserRequest
    {
        public string Email { get; set; } = null!;
        public string FirstName { get; set; } = null!;
        public string LastName { get; set; } = null!;
        public string Role { get; set; } = null!;
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
        public bool EmailVerified { get; set; }
    }
}
