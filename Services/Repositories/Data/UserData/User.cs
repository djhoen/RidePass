namespace Services.Repositories.Data.UserData
{
    public class User
    {
        public string Id { get; set; }
        public string? AboutMe { get; set; }
        public DateTime? BirthDate { get; set; }
        public string Email { get; set; }
        public string? DisplayName { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Phone { get; set; }
        public string? Password { get; set; }
        public string? ProfileImgUrl { get; set; }
        public int StatusId { get; set; }
        public string? Status { get; set; }
        public string? StripeId { get; set; }
        public int? BillingAddressId { get; set; }
        public int? ShippingAddressId { get; set; }
        public bool IsAccountInitialized { get; set; }
        public bool NeedsPassSetup { get; set; }
        public List<Permission>? Permissions { get; set; }
        public List<Role>? Roles { get; set; }

        public bool HasPermission(string permissionName)
        {
            return Permissions != null && Permissions.Any(x => x.Name == permissionName);
        }
    }
}
