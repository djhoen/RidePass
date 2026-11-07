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
        Task SetPhoto(Guid id, Guid tenantId, Guid? photoId);
        /// <summary>Sign-ups since <paramref name="sinceUtc"/>, newest first. Sign-ups whose sale's ticket has
        /// been paid since are stamped handled first.</summary>
        Task<List<KioskSignupRow>> ListSignups(Guid tenantId, DateTime sinceUtc, int take);
        /// <summary>A card sale was started for these sign-ups: they become handled once its ticket is paid.</summary>
        Task<int> SetPendingSale(Guid tenantId, IReadOnlyCollection<Guid> ids, Guid saleTicketId, Guid? byUserId, string saleRef);
        /// <summary>A paid (cash / free) sale used these sign-ups (only ones not handled yet are stamped).</summary>
        Task<int> MarkHandled(Guid tenantId, IReadOnlyCollection<Guid> ids, Guid? byUserId, string saleRef);
        /// <summary>Staff checked ID on a sign-up that matched an existing account: it now points at the
        /// profiles, signature and photo attached to it, and the held signature image is cleared.</summary>
        Task<bool> Confirm(Guid id, Guid tenantId, Guid? userId, Guid? dependentId, Guid? guardianUserId,
            Guid? signatureId, Guid? photoId, Guid? byUserId);
        /// <summary>Dismissed: also drops a held signature image.</summary>
        Task<bool> Dismiss(Guid id, Guid tenantId, Guid? byUserId);

        // ── Pushed steps (Script0311). Reads apply time (expiry, the claim lease) without writing. ──
        /// <summary>A new request, or null when the kiosk already has an open one (one at a time).</summary>
        Task<KioskPush?> CreatePush(KioskPush push);
        Task<KioskPush?> GetPush(Guid id, Guid tenantId);
        /// <summary>The kiosk's open request (waiting, on screen, or claimed within the lease), with WaiverUsable.</summary>
        Task<KioskPush?> GetActivePush(Guid tenantId, Guid kioskDeviceId);
        /// <summary>Every open request of the tenant, in one read.</summary>
        Task<List<KioskPush>> ListOpenPushes(Guid tenantId);
        /// <summary>The kiosk put it on screen.</summary>
        Task<bool> StartPush(Guid id, Guid tenantId, Guid kioskDeviceId);
        /// <summary>Takes the request for completion under a new claim (its own kiosk, before expiry; a stale
        /// claim can be taken again). Returns the row with ClaimId and any results of an earlier attempt.</summary>
        Task<KioskPush?> ClaimPush(Guid id, Guid tenantId, Guid kioskDeviceId);
        /// <summary>Recorded right after the write, so a retry reuses it instead of signing / saving again.</summary>
        Task<bool> RecordPushSignature(Guid id, Guid tenantId, Guid claimId, Guid signatureId);
        Task<bool> RecordPushPhoto(Guid id, Guid tenantId, Guid claimId, Guid photoId);
        Task<bool> FinishPush(Guid id, Guid tenantId, Guid claimId);
        /// <summary>Completion failed: the kiosk may try again (while it hasn't expired).</summary>
        Task ReleasePush(Guid id, Guid tenantId, Guid claimId);
        /// <summary>Ends an open request that can't be completed as sent (KioskPush.End*): staff send it again.</summary>
        Task<bool> EndPush(Guid id, Guid tenantId, string endReason);
        Task<bool> CancelPush(Guid id, Guid tenantId, Guid? byUserId);
        /// <summary>"Not me" / cancel / idle timeout at the kiosk.</summary>
        Task<bool> DeclinePush(Guid id, Guid tenantId, Guid kioskDeviceId);
        /// <summary>The kiosk is idle and asking for work.</summary>
        Task TouchPoll(Guid kioskDeviceId, Guid tenantId);
    }
}
