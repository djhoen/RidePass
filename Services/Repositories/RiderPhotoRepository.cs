using Services.Helpers.Interfaces;
using Services.Repositories.Data.RiderData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class RiderPhotoRepository : IRiderPhotoRepository
    {
        private const string Columns = @"
            id, tenant_id AS TenantId, owner_key AS OwnerKey, user_id AS UserId, owner_email AS OwnerEmail,
            rider_key AS RiderKey, storage_key AS StorageKey, taken_at AS TakenAt, taken_by_user_id AS TakenByUserId";

        private readonly IDbHelper _db;

        public RiderPhotoRepository(IDbHelper db) => _db = db;

        public async Task<RiderPhoto?> Get(Guid tenantId, string ownerKey, string riderKey)
        {
            var sql = $@"SELECT {Columns} FROM rider_photo
                         WHERE tenant_id = @tenantId AND owner_key = @ownerKey AND rider_key = @riderKey LIMIT 1";
            return (await _db.Query<RiderPhoto>(sql, new { tenantId, ownerKey, riderKey })).FirstOrDefault();
        }

        public async Task<RiderPhoto?> GetById(Guid id, Guid tenantId)
        {
            var sql = $"SELECT {Columns} FROM rider_photo WHERE id = @id AND tenant_id = @tenantId LIMIT 1";
            return (await _db.Query<RiderPhoto>(sql, new { id, tenantId })).FirstOrDefault();
        }

        public async Task<(RiderPhoto Photo, string? ReplacedStorageKey)> Upsert(Guid tenantId, string ownerKey,
            Guid? userId, string? ownerEmail, string riderKey, string storageKey, Guid? takenByUserId)
        {
            // One row per (tenant, owner, rider): a new photo replaces the old one (taken_at / taken_by
            // refreshed); the old storage key comes back so its image can be deleted.
            var sql = $@"
                WITH old AS (
                    SELECT storage_key FROM rider_photo
                    WHERE tenant_id = @tenantId AND owner_key = @ownerKey AND rider_key = @riderKey
                ), up AS (
                    INSERT INTO rider_photo (tenant_id, owner_key, user_id, owner_email, rider_key, storage_key,
                                             taken_at, taken_by_user_id)
                    VALUES (@tenantId, @ownerKey, @userId, @ownerEmail, @riderKey, @storageKey, now(), @takenByUserId)
                    ON CONFLICT (tenant_id, owner_key, rider_key) DO UPDATE
                       SET storage_key = EXCLUDED.storage_key, taken_at = now(),
                           taken_by_user_id = EXCLUDED.taken_by_user_id, updated_at = now()
                    RETURNING {Columns}
                )
                SELECT up.*, (SELECT storage_key FROM old) AS ReplacedStorageKey FROM up";
            var row = (await _db.Query<UpsertRow>(sql, new
            {
                tenantId, ownerKey, userId, ownerEmail, riderKey, storageKey, takenByUserId,
            })).First();
            return (row, row.ReplacedStorageKey);
        }

        private sealed class UpsertRow : RiderPhoto
        {
            public string? ReplacedStorageKey { get; set; }
        }
    }
}
