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
        Task<string?> GetDashboardConfig(Guid userId);
        Task SetDashboardConfig(Guid userId, string? jsonOrNull);
    }
}
