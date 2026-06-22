using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.User
{
    // Self-service profile update for the signed-in rider (the "My Profile" form). Covers the
    // editable identity fields; email is intentionally NOT here (changing it has auth +
    // verification implications and is handled separately).
    public class UpdateProfileRequest
    {
        [Required, MaxLength(120)] public string FirstName { get; set; } = null!;
        [Required, MaxLength(120)] public string LastName { get; set; } = null!;
        [MaxLength(40)] public string? Phone { get; set; }
        // The whole "My Profile" form saves in one call, so the emergency contact and photo URL
        // ride along here too. All optional (the form may leave them blank / clear them).
        [MaxLength(120)] public string? EmergencyContactName { get; set; }
        [MaxLength(40)] public string? EmergencyContactPhone { get; set; }
        [MaxLength(1024)] public string? ImageUrl { get; set; }
    }
}
