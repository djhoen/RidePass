using Services.Helpers.Interfaces;
using Services.Repositories.Data.RiderData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class RiderDependentRepository : IRiderDependentRepository
    {
        private const string Columns = @"
            id, tenant_id AS TenantId, guardian_user_id AS GuardianUserId, rider_user_id AS RiderUserId,
            first_name AS FirstName, last_name AS LastName, birthdate AS Birthdate, rider_key AS RiderKey,
            created_by_user_id AS CreatedByUserId, created_at AS CreatedAt, updated_at AS UpdatedAt";

        private readonly IDbHelper _db;

        public RiderDependentRepository(IDbHelper db) => _db = db;

        public async Task<RiderDependent> FindOrCreate(Guid tenantId, Guid guardianUserId, string firstName,
            string lastName, DateTime? birthdate, Guid? riderUserId, Guid? createdByUserId)
        {
            var riderKey = Services.Riders.IdCheckRules.Normalize($"{firstName} {lastName}");
            // One statement, so two cashiers adding the same child at once land on one row. The DO UPDATE
            // only fills gaps (and is what makes RETURNING hand back the existing row).
            var sql = $@"
                INSERT INTO rider_dependent
                    (tenant_id, guardian_user_id, rider_user_id, first_name, last_name, birthdate, rider_key, created_by_user_id)
                VALUES (@tenantId, @guardianUserId, @riderUserId, @firstName, @lastName, @birthdate::date, @riderKey, @createdByUserId)
                ON CONFLICT (tenant_id, guardian_user_id, rider_key) DO UPDATE
                    SET birthdate = COALESCE(rider_dependent.birthdate, EXCLUDED.birthdate),
                        rider_user_id = COALESCE(rider_dependent.rider_user_id, EXCLUDED.rider_user_id)
                RETURNING {Columns}";
            var rows = await _db.Query<RiderDependent>(sql, new
            {
                tenantId, guardianUserId, riderUserId,
                firstName = firstName.Trim(), lastName = lastName.Trim(),
                birthdate = birthdate?.Date, riderKey, createdByUserId,
            });
            return rows.First();
        }

        public async Task<RiderDependent?> GetById(Guid id, Guid tenantId)
        {
            var sql = $"SELECT {Columns} FROM rider_dependent WHERE id = @id AND tenant_id = @tenantId LIMIT 1";
            return (await _db.Query<RiderDependent>(sql, new { id, tenantId })).FirstOrDefault();
        }

        public async Task<List<RiderDependent>> ListForGuardian(Guid tenantId, Guid guardianUserId)
        {
            var sql = $@"SELECT {Columns} FROM rider_dependent
                         WHERE tenant_id = @tenantId AND guardian_user_id = @guardianUserId
                         ORDER BY lower(first_name), lower(last_name)";
            return (await _db.Query<RiderDependent>(sql, new { tenantId, guardianUserId })).ToList();
        }
    }
}
