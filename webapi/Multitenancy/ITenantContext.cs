using Services.Repositories.Data.TenantData;

namespace webapi.Multitenancy
{
    public interface ITenantContext
    {
        bool IsResolved { get; }
        Tenant Tenant { get; }
        Guid TenantId { get; }
        string Subdomain { get; }
    }
}
