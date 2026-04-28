using Services.Repositories.Data.PaymentData;

namespace Services.Repositories.Interfaces
{
    public interface IWaiverRepository
    {
        Task<TenantWaiver?> GetActive(Guid tenantId);
        Task<TenantWaiver?> GetById(Guid id, Guid tenantId);
        Task<TenantWaiver> PublishNewVersion(Guid tenantId, string title, string body);
        Task<RiderWaiverSignature?> GetSignature(Guid userId, Guid waiverId);
        Task<Guid> Sign(Guid tenantId, Guid userId, Guid waiverId, string? ipAddress);
    }
}
