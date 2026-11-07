using Services.Repositories.Data.RiderData;

namespace Services.Repositories.Interfaces
{
    /// <summary>Gate photos of riders, one per (tenant, owner, rider) (Script0307).</summary>
    public interface IRiderPhotoRepository
    {
        Task<RiderPhoto?> Get(Guid tenantId, string ownerKey, string riderKey);
        Task<RiderPhoto?> GetById(Guid id, Guid tenantId);

        /// <summary>Saves (or replaces) the rider's photo. Returns the row and the storage key of the
        /// photo it replaced, if any, so the caller can delete the old image.</summary>
        Task<(RiderPhoto Photo, string? ReplacedStorageKey)> Upsert(Guid tenantId, string ownerKey, Guid? userId,
            string? ownerEmail, string riderKey, string storageKey, Guid? takenByUserId);
    }
}
