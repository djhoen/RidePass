using Services.Helpers.Interfaces;
using Services.Repositories.Data.TaxData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class TenantTaxRepository : ITenantTaxRepository
    {
        private readonly IDbHelper _db;
        public TenantTaxRepository(IDbHelper db) => _db = db;

        private const string Cols = @"
            id, tenant_id AS TenantId, tax_kind AS TaxKind, rate_bps AS RateBps,
            prices_include_tax AS PricesIncludeTax, service_charge_taxable AS ServiceChargeTaxable,
            jurisdiction_label AS JurisdictionLabel, is_active AS IsActive,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        public async Task<TenantTaxRate?> GetByKind(Guid tenantId, string taxKind)
        {
            var sql = $@"SELECT {Cols} FROM tenant_tax_rate
                         WHERE tenant_id = @tenantId AND tax_kind = @taxKind";
            return (await _db.Query<TenantTaxRate>(sql, new { tenantId, taxKind })).FirstOrDefault();
        }

        public async Task<TenantTaxRate> Upsert(TenantTaxRate r)
        {
            var sql = $@"
                INSERT INTO tenant_tax_rate
                    (tenant_id, tax_kind, rate_bps, prices_include_tax, service_charge_taxable, jurisdiction_label, is_active)
                VALUES
                    (@TenantId, @TaxKind, @RateBps, @PricesIncludeTax, @ServiceChargeTaxable, @JurisdictionLabel, @IsActive)
                ON CONFLICT (tenant_id, tax_kind) DO UPDATE SET
                    rate_bps               = EXCLUDED.rate_bps,
                    prices_include_tax     = EXCLUDED.prices_include_tax,
                    service_charge_taxable = EXCLUDED.service_charge_taxable,
                    jurisdiction_label     = EXCLUDED.jurisdiction_label,
                    is_active              = EXCLUDED.is_active
                RETURNING {Cols}";
            return (await _db.Query<TenantTaxRate>(sql, r)).First();
        }
    }
}
