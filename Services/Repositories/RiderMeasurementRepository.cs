using Services.Helpers.Interfaces;
using Services.Repositories.Data.RiderData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class RiderMeasurementRepository : IRiderMeasurementRepository
    {
        private readonly IDbHelper _db;

        public RiderMeasurementRepository(IDbHelper db) => _db = db;

        public async Task<RiderMeasurement?> GetForUser(Guid userId, Guid tenantId)
        {
            const string sql = @"
                SELECT height_in AS HeightIn, weight_lb AS WeightLb, measured_at AS MeasuredAt
                FROM users
                WHERE id = @userId AND (tenant_id IS NULL OR tenant_id = @tenantId)
                LIMIT 1";
            return (await _db.Query<RiderMeasurement>(sql, new { userId, tenantId })).FirstOrDefault();
        }

        public async Task<RiderMeasurement?> GetForDependent(Guid dependentId, Guid tenantId)
        {
            const string sql = @"
                SELECT height_in AS HeightIn, weight_lb AS WeightLb, measured_at AS MeasuredAt
                FROM rider_dependent
                WHERE id = @dependentId AND tenant_id = @tenantId
                LIMIT 1";
            return (await _db.Query<RiderMeasurement>(sql, new { dependentId, tenantId })).FirstOrDefault();
        }

        public async Task<List<DependentMeasurement>> ListDependents(Guid tenantId, Guid guardianUserId)
        {
            const string sql = @"
                SELECT id, first_name AS FirstName, last_name AS LastName, birthdate AS Birthdate,
                       height_in AS HeightIn, weight_lb AS WeightLb, measured_at AS MeasuredAt
                FROM rider_dependent
                WHERE tenant_id = @tenantId AND guardian_user_id = @guardianUserId
                ORDER BY lower(first_name), lower(last_name)";
            return (await _db.Query<DependentMeasurement>(sql, new { tenantId, guardianUserId })).ToList();
        }

        public async Task<bool> SaveForUser(Guid userId, Guid tenantId, int heightIn, int weightLb)
        {
            const string sql = @"
                UPDATE users
                   SET height_in = @heightIn, weight_lb = @weightLb, measured_at = now(), updated_at = now()
                 WHERE id = @userId AND (tenant_id IS NULL OR tenant_id = @tenantId)";
            return await _db.Execute(sql, new { userId, tenantId, heightIn, weightLb }) > 0;
        }

        public async Task<bool> SaveForDependent(Guid dependentId, Guid tenantId, Guid? guardianUserId, int heightIn, int weightLb)
        {
            const string sql = @"
                UPDATE rider_dependent
                   SET height_in = @heightIn, weight_lb = @weightLb, measured_at = now()
                 WHERE id = @dependentId AND tenant_id = @tenantId
                   AND (@guardianUserId::uuid IS NULL OR guardian_user_id = @guardianUserId)";
            return await _db.Execute(sql, new { dependentId, tenantId, guardianUserId, heightIn, weightLb }) > 0;
        }

        public async Task SnapshotOnShopRental(Guid rentalId, Guid tenantId, int heightIn, int weightLb)
        {
            const string sql = @"
                UPDATE shop_rental SET rider_height_in = @heightIn, rider_weight_lb = @weightLb
                 WHERE id = @rentalId AND tenant_id = @tenantId";
            await _db.Execute(sql, new { rentalId, tenantId, heightIn, weightLb });
        }

        public async Task<RiderMeasurement?> GetShopRentalSnapshot(Guid rentalId, Guid tenantId)
        {
            const string sql = @"
                SELECT rider_height_in AS HeightIn, rider_weight_lb AS WeightLb, created_at AS MeasuredAt
                FROM shop_rental
                WHERE id = @rentalId AND tenant_id = @tenantId
                LIMIT 1";
            return (await _db.Query<RiderMeasurement>(sql, new { rentalId, tenantId })).FirstOrDefault();
        }

        public async Task HoldOnKioskSignup(Guid signupId, Guid tenantId, int heightIn, int weightLb)
        {
            const string sql = @"
                UPDATE kiosk_signup SET height_in = @heightIn, weight_lb = @weightLb
                 WHERE id = @signupId AND tenant_id = @tenantId";
            await _db.Execute(sql, new { signupId, tenantId, heightIn, weightLb });
        }

        public async Task<RiderMeasurement?> GetHeldOnKioskSignup(Guid signupId, Guid tenantId)
        {
            const string sql = @"
                SELECT height_in AS HeightIn, weight_lb AS WeightLb, created_at AS MeasuredAt
                FROM kiosk_signup
                WHERE id = @signupId AND tenant_id = @tenantId
                LIMIT 1";
            return (await _db.Query<RiderMeasurement>(sql, new { signupId, tenantId })).FirstOrDefault();
        }
    }
}
