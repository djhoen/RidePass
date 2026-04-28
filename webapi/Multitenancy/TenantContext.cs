using Services.Repositories.Data.TenantData;

namespace webapi.Multitenancy
{
    public class TenantContext : ITenantContext
    {
        private Tenant? _tenant;

        public bool IsResolved => _tenant is not null;

        public Tenant Tenant => _tenant
            ?? throw new InvalidOperationException(
                "Tenant has not been resolved for this request. Ensure TenantResolutionMiddleware runs and the request targets a tenant subdomain.");

        public Guid TenantId => Tenant.Id;

        public string Subdomain => Tenant.Subdomain;

        public void SetTenant(Tenant tenant)
        {
            _tenant = tenant;
        }
    }
}
