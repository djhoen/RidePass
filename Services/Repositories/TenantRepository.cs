using Services.Helpers.Interfaces;
using Services.Repositories.Data.TenantData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class TenantRepository : ITenantRepository
    {
        private const string SelectColumns = @"
            id, subdomain, display_name AS DisplayName, status, timezone,
            require_reservation_for_passes AS RequireReservationForPasses,
            address_line AS AddressLine, city, region, postal_code AS PostalCode, country,
            latitude, longitude,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        private readonly IDbHelper _db;

        public TenantRepository(IDbHelper db)
        {
            _db = db;
        }

        public async Task<Tenant?> GetBySubdomain(string subdomain)
        {
            var sql = $"SELECT {SelectColumns} FROM tenant WHERE subdomain = @subdomain LIMIT 1";
            var result = await _db.Query<Tenant>(sql, new { subdomain });
            return result.FirstOrDefault();
        }

        public async Task<Tenant?> GetById(Guid id)
        {
            var sql = $"SELECT {SelectColumns} FROM tenant WHERE id = @id LIMIT 1";
            var result = await _db.Query<Tenant>(sql, new { id });
            return result.FirstOrDefault();
        }

        public async Task<Guid> Create(Tenant tenant)
        {
            const string sql = @"
                INSERT INTO tenant (subdomain, display_name, status, timezone)
                VALUES (@Subdomain, @DisplayName, @Status, @Timezone)
                RETURNING id";
            var result = await _db.Query<Guid>(sql, tenant);
            return result.First();
        }

        public async Task UpdateTimezone(Guid tenantId, string timezone)
        {
            const string sql = "UPDATE tenant SET timezone = @timezone WHERE id = @tenantId";
            await _db.Execute(sql, new { tenantId, timezone });
        }

        public async Task UpdateRequireReservation(Guid tenantId, bool require)
        {
            const string sql = "UPDATE tenant SET require_reservation_for_passes = @require WHERE id = @tenantId";
            await _db.Execute(sql, new { tenantId, require });
        }

        public async Task UpdateLocation(Guid tenantId, string? addressLine, string? city, string? region,
            string? postalCode, string? country, double? latitude, double? longitude)
        {
            const string sql = @"
                UPDATE tenant
                SET address_line = @addressLine,
                    city = @city,
                    region = @region,
                    postal_code = @postalCode,
                    country = @country,
                    latitude = @latitude,
                    longitude = @longitude
                WHERE id = @tenantId";
            await _db.Execute(sql, new { tenantId, addressLine, city, region, postalCode, country, latitude, longitude });
        }

        public async Task<List<Tenant>> ListAll()
        {
            var sql = $"SELECT {SelectColumns} FROM tenant ORDER BY subdomain";
            var result = await _db.Query<Tenant>(sql);
            return result.ToList();
        }
    }
}
