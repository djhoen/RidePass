using Services.Repositories.Interfaces;
using Services.Riders;
using Services.Waivers;

namespace webapi.Helpers
{
    /// <summary>What a kiosk sign-up attached to a profile.</summary>
    public sealed record KioskAttachResult(Guid? SignatureId, Guid? PhotoId, bool KeptExistingPhoto, string? Error);

    /// <summary>
    /// Attaches a kiosk sign-up's waiver signature and photo to the profile it was saved to (Script0310):
    /// at the kiosk for a brand-new profile, or when a cashier confirms a sign-up that matched an existing
    /// account, after checking ID. One path for both, through the venue signing rules
    /// (AccountWaiverSigning) and the private rider photo store under the gate's key for that rider.
    /// </summary>
    public interface IKioskSignupWriter
    {
        Task<KioskAttachResult> Attach(Guid tenantId, string? timezone, OtherRiderPlan plan, OtherRiderSaved saved,
            Guid? waiverId, string? signatureDataUrl, string? ip, string? guardianName, string? guardianPhone,
            string? guardianEmail, (byte[] Bytes, string Extension)? photo, bool replacePhotoOnFile, Guid? staffUserId);
    }

    public class KioskSignupWriter : IKioskSignupWriter
    {
        private readonly IUserRepository _users;
        private readonly IWaiverRepository _waivers;
        private readonly IWaiverCheckInGate _gate;
        private readonly IRiderPhotos _photos;

        public KioskSignupWriter(IUserRepository users, IWaiverRepository waivers, IWaiverCheckInGate gate, IRiderPhotos photos)
        {
            _users = users;
            _waivers = waivers;
            _gate = gate;
            _photos = photos;
        }

        public async Task<KioskAttachResult> Attach(Guid tenantId, string? timezone, OtherRiderPlan plan, OtherRiderSaved saved,
            Guid? waiverId, string? signatureDataUrl, string? ip, string? guardianName, string? guardianPhone,
            string? guardianEmail, (byte[] Bytes, string Extension)? photo, bool replacePhotoOnFile, Guid? staffUserId)
        {
            var riderAccount = saved.RiderUserId is Guid ruid ? BuyerAccounts.ForTenant(await _users.GetById(ruid), tenantId) : null;
            var guardian = saved.GuardianUserId is Guid gid ? BuyerAccounts.ForTenant(await _users.GetById(gid), tenantId) : null;

            Guid? signatureId = null;
            if (waiverId is Guid wid && !string.IsNullOrEmpty(signatureDataUrl))
            {
                (Guid? Id, string? Error) signed;
                if (riderAccount is not null)
                    // An adult signs for themselves; a minor's own account takes their guardian's signature.
                    signed = await AccountWaiverSigning.SignForAccount(_waivers, _gate, tenantId, timezone, riderAccount, wid, ip,
                        signatureDataUrl, plan.IsMinor ? guardianName : null, plan.IsMinor ? guardianPhone : null);
                else if (plan.IsMinor)
                    signed = await AccountWaiverSigning.SignForDependent(_waivers, tenantId, wid, ip, signatureDataUrl,
                        plan.FirstName, plan.LastName, plan.Birthdate, guardianName, guardianPhone, guardianEmail);
                else
                    signed = (null, null);
                if (signed.Error is not null) return new KioskAttachResult(null, null, false, signed.Error);
                signatureId = signed.Id;
            }

            Guid? photoId = null;
            var kept = false;
            if (photo is { } p)
            {
                // Where the gate looks for this rider: their own account, or the guardian + the dependent's id.
                var keying = plan.IsMinor
                    ? (guardian is not null && saved.DependentId is Guid depId ? TicketRiderKeys.ForDependent(guardian, depId) : null)
                    : (riderAccount is not null ? TicketRiderKeys.ForRiderAccount(riderAccount) : null);
                if (keying is not null)
                {
                    var key = RiderPhotoRules.KeyFor(keying, null, plan.FirstName, plan.LastName, "kiosk", Guid.NewGuid());
                    var onFile = await _photos.Find(tenantId, key);
                    if (onFile is not null && !replacePhotoOnFile)
                    {
                        // A photo is already on file for them: kept unless staff chose to replace it.
                        photoId = onFile.Id;
                        kept = true;
                    }
                    else
                    {
                        photoId = (await _photos.RecordForKey(tenantId, key, p.Bytes, p.Extension, staffUserId)).Id;
                    }
                }
            }
            return new KioskAttachResult(signatureId, photoId, kept, null);
        }
    }
}
