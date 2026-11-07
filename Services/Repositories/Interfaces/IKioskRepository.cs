using Services.Repositories.Data.KioskData;

namespace Services.Repositories.Interfaces
{
    /// <summary>Front-desk kiosks and their sign-ups (Script0310), per tenant.</summary>
    public interface IKioskRepository
    {
        Task<KioskDevice> CreateDevice(Guid tenantId, string name, string pairingCodeHash, DateTime pairingExpiresAtUtc, Guid? createdByUserId);
        Task<List<KioskDevice>> ListDevices(Guid tenantId);
        Task<KioskDevice?> GetDevice(Guid id, Guid tenantId);
        Task<bool> RenameDevice(Guid id, Guid tenantId, string name);
        /// <summary>Revokes the device: its secret and any pending pairing code stop working at once.</summary>
        Task<bool> RevokeDevice(Guid id, Guid tenantId, Guid? byUserId);
        /// <summary>A new one-time pairing code for a device that isn't revoked (re-pairing a tablet).</summary>
        Task<bool> SetPairingCode(Guid id, Guid tenantId, string pairingCodeHash, DateTime expiresAtUtc);
        /// <summary>Swaps a valid, unexpired pairing code for the device's new secret (one statement, single use).
        /// The previous secret, if any, stops working. Null when the code is wrong, used or expired.</summary>
        Task<KioskDevice?> RedeemPairingCode(Guid tenantId, string pairingCodeHash, string credentialHash);
        /// <summary>The paired, not revoked device holding this secret here.</summary>
        Task<KioskDevice?> GetByCredential(Guid tenantId, string credentialHash);
        /// <summary>Stamps last_seen_at (at most once a minute).</summary>
        Task TouchLastSeen(Guid id, Guid tenantId);

        Task<Guid> CreateSignup(KioskSignup signup);
        Task<KioskSignup?> GetSignup(Guid id, Guid tenantId);
        /// <summary>Sign-ups since <paramref name="sinceUtc"/>, newest first.</summary>
        Task<List<KioskSignupRow>> ListSignups(Guid tenantId, DateTime sinceUtc, int take);
        /// <summary>A sale used these sign-ups (only ones not handled yet are stamped).</summary>
        Task<int> MarkHandled(Guid tenantId, IReadOnlyCollection<Guid> ids, Guid? byUserId, string saleRef);
        Task<bool> Dismiss(Guid id, Guid tenantId, Guid? byUserId);
    }
}
