using Services.Repositories.Data.UserData;

namespace Services.Repositories.Interfaces
{
    public interface IUserRepository
    {
        Task<User?> GetByEmail(Guid tenantId, string email);
        Task<User?> GetGlobalByEmail(string email);
        Task<User?> GetById(Guid id);

        /// <summary>
        /// Reverse-lookup for inbound SMS: given a Twilio E.164 phone (e.g.
        /// "+15551234567"), find the user whose loosely-stored phone matches
        /// after normalization. Uses the expression index from
        /// Script0088_UserPhoneE164Index. Prefers global rider accounts
        /// (tenant_id IS NULL) when multiple users share a number, since the
        /// Inbox is about customer threads — staff phones lose to riders.
        /// </summary>
        Task<User?> GetByPhoneE164(string phoneE164);
        Task<Guid> Create(User user);
        Task<bool> AnySuperAdminExists();
        Task<List<User>> SearchAll(string? query, int take = 50);
        Task<List<User>> ListByTenant(Guid tenantId);
        Task<List<User>> ListSuperAdmins();
        Task<List<User>> ListTenantUsersByRole(Guid tenantId, string role);
        Task UpdateRole(Guid id, string role);
        Task UpdateRoles(Guid id, string primaryRole, string[] roles);
        Task UpdateStatus(Guid id, string status);
        Task UpdatePasswordHash(Guid id, string passwordHash);
        Task SuperAdminUpdateUser(User u);
        Task UpdateEmergencyContact(Guid userId, string name, string phone);
        Task UpdatePhone(Guid userId, string? phone);
        Task UpdateProfile(Guid userId, string firstName, string lastName, string? phone);
        Task UpdateImageUrl(Guid userId, string? imageUrl);
        Task UpdateRacerInfo(Guid userId, string? bike, string? raceNumber);
        Task UpdateBirthdate(Guid userId, DateTime birthdate);
        Task SetEmailVerificationToken(Guid userId, string tokenHash, DateTime expiresAtUtc);
        Task<User?> GetByEmailVerificationTokenHash(string tokenHash);
        Task MarkEmailVerified(Guid userId);
        Task UpdateAddress(Guid userId, string? addressLine, string? addressLine2,
            string? city, string? state, string? postalCode, string? country);
        Task<string?> GetDashboardConfig(Guid userId);
        Task SetDashboardConfig(Guid userId, string? jsonOrNull);
    }
}
