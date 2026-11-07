using Services.Repositories.Data.RiderData;

namespace Services.Repositories.Interfaces
{
    /// <summary>Gate photos of riders, one per (tenant, owner, rider) (Script0307).</summary>
    public interface IRiderPhotoRepository
    {
        Task<RiderPhoto?> Get(Guid tenantId, string ownerKey, string riderKey);
        Task<RiderPhoto?> GetById(Guid id, Guid tenantId);

        /// <summary>Saves (or replaces) the rider's photo in one locked transaction. Returns the row and the storage key of the
        /// photo it replaced, if any, so the caller can delete the old image.</summary>
        Task<(RiderPhoto Photo, string? ReplacedStorageKey)> Upsert(Guid tenantId, string ownerKey, Guid? userId,
            string? ownerEmail, string riderKey, string storageKey, Guid? takenByUserId);

        /// <summary>Images queued for deletion when their row was deleted (directly, or by the account /
        /// tenant cascade) or re-pointed. A system queue across tenants, oldest first.</summary>
        Task<List<PrivateImageTrashItem>> ListTrash(int take);
        Task RemoveFromTrash(long id, Guid tenantId);
        /// <summary>A live rider_photo row still points at this image (never delete it then).</summary>
        Task<bool> IsReferenced(Guid tenantId, string storageKey);
    }
}
