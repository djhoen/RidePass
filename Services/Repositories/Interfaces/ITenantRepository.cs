using Services.Repositories.Data.TenantData;

namespace Services.Repositories.Interfaces
{
    public interface ITenantRepository
    {
        Task<Tenant?> GetBySubdomain(string subdomain);
        Task<Tenant?> GetById(Guid id);
        Task<Guid> Create(Tenant tenant);
        Task UpdateTimezone(Guid tenantId, string timezone);
        Task UpdateRequireReservation(Guid tenantId, bool require);
        Task UpdateLocation(Guid tenantId, string? addressLine, string? city, string? region,
            string? postalCode, string? country, double? latitude, double? longitude);
        Task<List<Tenant>> ListAll();
    }
}
