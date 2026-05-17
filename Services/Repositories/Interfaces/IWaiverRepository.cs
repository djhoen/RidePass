using Services.Repositories.Data.PaymentData;

namespace Services.Repositories.Interfaces
{
    public interface IWaiverRepository
    {
        /// <summary>Tenant default fallback (newest non-expired active row).</summary>
        Task<TenantWaiver?> GetActive(Guid tenantId);
        Task<TenantWaiver?> GetById(Guid id, Guid tenantId);
        Task<List<TenantWaiver>> ListByTenant(Guid tenantId);
        Task<TenantWaiver> Create(Guid tenantId, string name, string title, string body,
            bool isActive, DateTime? expiresAt);
        Task Update(Guid id, Guid tenantId, string name, string title, string body,
            bool isActive, DateTime? expiresAt);
        Task<TenantWaiver> PublishNewVersion(Guid tenantId, string title, string body);
        Task<RiderWaiverSignature?> GetSignature(Guid userId, Guid waiverId);
        Task<Guid> Sign(Guid tenantId, Guid userId, Guid waiverId, string? ipAddress, string? signatureDataUrl,
            bool signedByParent, string? parentName, string? parentPhone);

        /// <summary>Email lookup for guest spectator buyers — checks whether this
        /// email has already signed THIS waiver for themselves (not on behalf of a child).</summary>
        Task<RiderWaiverSignature?> GetSignatureBySignerEmailForSelf(string email, Guid waiverId);

        /// <summary>Captures a guest spectator signature with full attendee details.
        /// One row per attending spectator — purchaser signs for themselves once,
        /// then again for each minor on the same purchase.</summary>
        Task<Guid> SignSpectator(Guid tenantId, Guid waiverId, string? ipAddress,
            string signatureDataUrl, string signerEmail, string signerName,
            string spectatorFirstName, string spectatorLastName, DateTime? spectatorBirthdate,
            bool signedByParent, string? parentName, string? parentPhone);
    }
}
