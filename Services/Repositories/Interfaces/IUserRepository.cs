using Services.Repositories.Data.UserData;

namespace Services.Repositories.Interfaces
{
    public interface IUserRepository
    {
        Task<User?> GetByEmail(Guid tenantId, string email);
        Task<User?> GetGlobalByEmail(string email);
        Task<User?> GetById(Guid id);
        Task<Guid> Create(User user);
        Task<bool> AnySuperAdminExists();
        Task<List<User>> SearchAll(string? query, int take = 50);
        Task<List<User>> ListByTenant(Guid tenantId);
        Task<List<User>> ListSuperAdmins();
        Task<List<User>> ListTenantUsersByRole(Guid tenantId, string role);
        Task UpdateRole(Guid id, string role);
        Task UpdateStatus(Guid id, string status);
        Task UpdatePasswordHash(Guid id, string passwordHash);
        Task UpdateEmergencyContact(Guid userId, string name, string phone);
        Task UpdatePhone(Guid userId, string? phone);
        Task UpdateRacerInfo(Guid userId, string? bike, string? raceNumber);
        Task UpdateBirthdate(Guid userId, DateTime birthdate);
        Task UpdateAddress(Guid userId, string? addressLine, string? addressLine2,
            string? city, string? state, string? postalCode, string? country);
        Task<string?> GetDashboardConfig(Guid userId);
        Task SetDashboardConfig(Guid userId, string? jsonOrNull);
    }
}
