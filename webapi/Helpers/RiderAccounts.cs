using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Services.Repositories.Data.UserData;
using webapi.Controllers;

namespace webapi.Helpers
{
    /// <summary>
    /// The one way staff create a rider's account (Gate Sale new customer, Gate Sale "another rider" and their
    /// guardian), and the shared checks for what they type: email ([EmailAddress], as the request DTOs use),
    /// emergency contact (UserController's rule).
    /// </summary>
    public static class RiderAccounts
    {
        /// <summary>A new platform rider account (tenant_id NULL: one account works at every tenant) with an
        /// unguessable password the rider never sees; they claim it later via password reset.</summary>
        public static User NewRider(IPasswordHasher<User> hasher, string email, string firstName, string lastName,
            DateTime? birthdate, string? phone, string? emergencyContactName, string? emergencyContactPhone)
        {
            Span<byte> bytes = stackalloc byte[32];
            RandomNumberGenerator.Fill(bytes);
            var user = new User
            {
                TenantId = null,
                Email = email.Trim(),
                FirstName = firstName.Trim(),
                LastName = lastName.Trim(),
                Role = "rider",
                Status = "active",
                Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
                Birthdate = birthdate?.Date,
                EmergencyContactName = string.IsNullOrWhiteSpace(emergencyContactName) ? null : emergencyContactName.Trim(),
                EmergencyContactPhone = string.IsNullOrWhiteSpace(emergencyContactPhone) ? null : emergencyContactPhone.Trim(),
            };
            user.PasswordHash = hasher.HashPassword(user, Convert.ToHexString(bytes));
            return user;
        }

        /// <summary>The codebase's email rule (the [EmailAddress] attribute on the request DTOs).</summary>
        public static bool IsValidEmail(string? email) =>
            !string.IsNullOrWhiteSpace(email) && email.Trim().Length <= 200 && new EmailAddressAttribute().IsValid(email.Trim());

        /// <summary>A phone number staff typed is usable (UserController's rule: at least 7 digits).</summary>
        public static bool IsValidPhone(string? phone) => UserController.DigitsOnly(phone ?? string.Empty).Length >= 7;

        /// <summary>CreateRider's emergency-contact rule: a name and a phone number of at least 7 digits.</summary>
        public static string? EmergencyContactError(string? name, string? phone) =>
            string.IsNullOrWhiteSpace(name) || !IsValidPhone(phone)
                ? "Please enter a valid emergency contact name and phone number." : null;
    }
}
