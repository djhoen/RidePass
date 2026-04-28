using Services.Repositories.Data.TenantData;

namespace Services.Repositories.Interfaces
{
    public interface ITenantBrandingRepository
    {
        Task<TenantBranding?> GetByTenantId(Guid tenantId);
        Task UpdateMetadata(Guid tenantId, string primaryColor, string secondaryColor, string accentColor,
                            string? tagline, string themeMode);
        Task UpdateImageUrl(Guid tenantId, string kind, string? url);
    }
}
