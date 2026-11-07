using Dapper;
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

        public Task<(RiderPhoto Photo, string? ReplacedStorageKey)> Upsert(Guid tenantId, string ownerKey,
            Guid? userId, string? ownerEmail, string riderKey, string storageKey, Guid? takenByUserId)
        {
            // One transaction: serialize writers for this (tenant, owner, rider) with a transaction-scoped
            // advisory lock (it also covers the very first photo, when there is no row to lock yet), lock
            // and read the current row (FOR UPDATE), then insert or replace it. So the old key returned is
            // exactly the image this write replaced, and two staff taking a photo at once can't orphan one.
            // The caller deletes that old image only after this commits.
            return _db.WithTransaction(async (conn, tx) =>
            {
                var args = new { tenantId, ownerKey, userId, ownerEmail, riderKey, storageKey, takenByUserId };
                await conn.ExecuteAsync(
                    "SELECT pg_advisory_xact_lock(hashtextextended(@tenantId::text || '|' || @ownerKey || '|' || @riderKey, 7307))",
                    args, tx);
                var old = await conn.QueryFirstOrDefaultAsync<string?>(@"
                    SELECT storage_key FROM rider_photo
                    WHERE tenant_id = @tenantId AND owner_key = @ownerKey AND rider_key = @riderKey
                    FOR UPDATE", args, tx);
                var photo = await conn.QuerySingleAsync<RiderPhoto>($@"
                    INSERT INTO rider_photo (tenant_id, owner_key, user_id, owner_email, rider_key, storage_key,
                                             taken_at, taken_by_user_id)
                    VALUES (@tenantId, @ownerKey, @userId, @ownerEmail, @riderKey, @storageKey, now(), @takenByUserId)
                    ON CONFLICT (tenant_id, owner_key, rider_key) DO UPDATE
                       SET storage_key = EXCLUDED.storage_key, taken_at = now(),
                           taken_by_user_id = EXCLUDED.taken_by_user_id, updated_at = now()
                    RETURNING {Columns}", args, tx);
                return (photo, old);
            });
        }

        // System queue (Script0307 trigger), not one tenant's data: every tenant's dropped images, oldest first.
        public async Task<List<PrivateImageTrashItem>> ListTrash(int take)
        {
            const string sql = @"SELECT id, tenant_id AS TenantId, storage_key AS StorageKey
                                 FROM private_image_trash ORDER BY id LIMIT @take";
            return (await _db.Query<PrivateImageTrashItem>(sql, new { take })).ToList();
        }

        public async Task RemoveFromTrash(long id, Guid tenantId) =>
            await _db.Execute("DELETE FROM private_image_trash WHERE id = @id AND tenant_id = @tenantId", new { id, tenantId });

        public async Task<bool> IsReferenced(Guid tenantId, string storageKey)
        {
            var n = await _db.Query<int>(
                "SELECT COUNT(*)::int FROM rider_photo WHERE tenant_id = @tenantId AND storage_key = @storageKey",
                new { tenantId, storageKey });
            return n.First() > 0;
        }
    }
}
