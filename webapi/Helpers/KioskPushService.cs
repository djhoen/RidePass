using Services.Helpers;
using Services.Repositories.Data.KioskData;
using Services.Repositories.Interfaces;
using Services.Riders;
using Services.Waivers;
using webapi.Controllers.API.Data.Kiosk;
using webapi.Multitenancy;

namespace webapi.Helpers
{
    /// <summary>
    /// Waiver and photo steps pushed to a kiosk for one rider (Script0311). The target is decided exactly as the
    /// gate decides a signing link (ResolveSignTarget + SignRequestMode.ForTarget: guardian vs adult, the
    /// required document, where the signature goes) and the photo goes where the gate looks for that rider
    /// (IRiderPhotos for the ticket / pass, TicketRiderKeys for a kiosk sign-up's profile). Completion uses the
    /// same writes as a signing link (a registrant signature + GateWaiverLinking) and the gate's photo store.
    /// </summary>
    public interface IKioskPushService
    {
        /// <summary>The request to store for this target and these steps, or why it can't be sent.</summary>
        Task<(KioskPush? Push, string? Error)> Build(Guid tenantId, CreateKioskPushRequest req, Guid? staffUserId);

        /// <summary>Checks what the kiosk sent before anything is claimed (so a typo can be fixed and retried).</summary>
        string? ValidateCompletion(KioskPush push, KioskPushCompleteRequest req);

        /// <summary>Signs / saves for a claimed request. Returns the signature and photo, or an error (then the
        /// caller releases the request so the kiosk can try again).</summary>
        Task<(Guid? SignatureId, Guid? PhotoId, string? Error)> Complete(KioskPush push, KioskPushCompleteRequest req, string? ip);
    }

    public class KioskPushService : IKioskPushService
    {
        public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
        private const string TryStaff = "This couldn't be saved here. Please ask staff to send it again.";

        private readonly IWaiverCheckInGate _gate;
        private readonly IWaiverRepository _waivers;
        private readonly IEventTicketPurchaseRepository _tickets;
        private readonly ISeasonPassRepository _passes;
        private readonly IKioskRepository _kiosks;
        private readonly IUserRepository _users;
        private readonly IRiderDependentRepository _dependents;
        private readonly IRiderPhotos _photos;
        private readonly ITenantContext _tenantContext;

        public KioskPushService(IWaiverCheckInGate gate, IWaiverRepository waivers, IEventTicketPurchaseRepository tickets,
            ISeasonPassRepository passes, IKioskRepository kiosks, IUserRepository users, IRiderDependentRepository dependents,
            IRiderPhotos photos, ITenantContext tenantContext)
        {
            _gate = gate;
            _waivers = waivers;
            _tickets = tickets;
            _passes = passes;
            _kiosks = kiosks;
            _users = users;
            _dependents = dependents;
            _photos = photos;
            _tenantContext = tenantContext;
        }

        public async Task<(KioskPush? Push, string? Error)> Build(Guid tenantId, CreateKioskPushRequest req, Guid? staffUserId)
        {
            if (!req.Waiver && !req.Photo) return (null, "Choose the waiver, the photo, or both.");
            var push = new KioskPush
            {
                TenantId = tenantId,
                KioskDeviceId = req.KioskDeviceId,
                CreatedByUserId = staffUserId,
                ExpiresAt = DateTime.UtcNow.Add(Lifetime),
                WantWaiver = req.Waiver,
                WantPhoto = req.Photo,
                TargetKind = req.TargetKind,
            };

            if (req.TargetKind is "ticket" or "season_pass")
            {
                if (req.PurchaseId is not Guid pid) return (null, "Which ticket or pass?");
                var target = await _gate.ResolveSignTarget(tenantId,
                    req.TargetKind == "ticket" ? pid : null, req.TargetKind == "season_pass" ? pid : null,
                    req.TargetKind == "season_pass" ? req.EventId : null);
                if (target is null) return (null, req.TargetKind == "ticket" ? "Ticket not found." : "Pass not found.");
                var d = target.Decision;
                push.EventTicketPurchaseId = target.TicketId;
                push.SeasonPassPurchaseId = target.PassPurchaseId;
                push.EventId = req.TargetKind == "season_pass" ? req.EventId : target.EventId;
                var mode = SignRequestMode.ForTarget(target);
                push.RiderFirstName = mode.RiderFirstName ?? FirstWord(target.DisplayName);
                if (req.Waiver)
                {
                    // Exactly the gate's own fix: only a waiver block a new signature can clear.
                    if (d.BlockReason is null || !d.Required || d.Satisfied)
                        return (null, $"{target.DisplayName} doesn't need a new waiver signature.");
                    if (d.RegistrationIncomplete || d.FixTarget is null || d.RequiredWaiverId is null)
                        return (null, $"A signature won't fix this: {d.BlockReason}");
                    if (mode.SignAs is null)
                        return (null, $"{target.DisplayName}'s birthdate isn't known, so the kiosk can't tell who signs. Send a signing link instead.");
                    push.SignMode = mode.SignAs;
                    push.WaiverId = d.RequiredWaiverId;
                }
                if (req.Photo && req.TargetKind == "ticket" && !d.RiderAudience)
                    return (null, "Photos are for riders; this is a spectator ticket.");
                return (push, null);
            }

            if (req.TargetKind == "kiosk_signup")
            {
                if (req.KioskSignupId is not Guid sid) return (null, "Which sign-up?");
                var s = await _kiosks.GetSignup(sid, tenantId);
                if (s is null) return (null, "Sign-up not found.");
                if (s.MatchPending) return (null, "Check their ID and confirm the sign-up first.");
                if (s.DismissedAt is not null) return (null, "That sign-up was dismissed.");
                if (s.UserId is null && s.DependentId is null) return (null, "That sign-up has no profile yet.");
                push.KioskSignupId = s.Id;
                push.RiderFirstName = s.FirstName;
                if (req.Waiver)
                {
                    var active = await _waivers.GetActive(tenantId);
                    if (active is null || (active.ExpiresAt is { } exp && exp <= DateTime.UtcNow))
                        return (null, "There is no active waiver to sign.");
                    var today = TenantClock.LocalDate(_tenantContext.Tenant.Timezone, DateTime.UtcNow);
                    var minor = GuardianWaiverRule.ToDateOnly(s.Birthdate) is { } bd ? CounterRiderRules.IsMinor(bd, today) : s.IsMinor;
                    push.SignMode = minor ? "guardian" : "adult";
                    push.WaiverId = active.Id;
                }
                return (push, null);
            }
            return (null, "Unknown target.");
        }

        public string? ValidateCompletion(KioskPush push, KioskPushCompleteRequest req)
        {
            if (push.WantWaiver)
            {
                if (req.WaiverId != push.WaiverId) return "The waiver changed, please ask staff to send it again.";
                if (!req.AgreedToWaiver) return "Read the waiver and tick that you agree before signing.";
                if (!SignatureImages.IsValidPngDataUrl(req.SignatureDataUrl)) return "Sign the waiver with your finger in the box.";
                if (push.SignMode == "guardian"
                    && (string.IsNullOrWhiteSpace(req.GuardianName) || !RiderAccounts.IsValidPhone(req.GuardianPhone)))
                    return AccountWaiverSigning.MinorNeedsGuardian;
            }
            if (push.WantPhoto && RiderPhotoRules.DecodePhoto(req.PhotoDataUrl) is null)
                return "Take the photo to finish.";
            return null;
        }

        public async Task<(Guid? SignatureId, Guid? PhotoId, string? Error)> Complete(KioskPush push, KioskPushCompleteRequest req, string? ip)
        {
            var tenantId = push.TenantId;
            var guardianName = req.GuardianName?.Trim();
            var guardianPhone = req.GuardianPhone?.Trim();
            Guid? signatureId = null, photoId = null;

            if (push.TargetKind is "ticket" or "season_pass")
            {
                var target = await _gate.ResolveSignTarget(tenantId, push.EventTicketPurchaseId, push.SeasonPassPurchaseId, push.EventId);
                if (target is null) return (null, null, TryStaff);
                if (push.WantWaiver && push.WaiverId is Guid waiverId)
                {
                    var d = target.Decision;
                    // Already fixed meanwhile (e.g. a signing link): nothing to sign.
                    if (d.BlockReason is not null && !d.Satisfied)
                    {
                        if (d.RequiredWaiverId != waiverId || d.FixTarget is null) return (null, null, TryStaff);
                        // The SignByToken rules: the gate's own view of the rider decides who signs (a minor's is
                        // their parent's or guardian's), and the signature names the rider.
                        var mode = SignRequestMode.ForTarget(target);
                        var isMinor = mode.KnownMinor ?? push.SignMode == "guardian";
                        if (isMinor != (push.SignMode == "guardian")) return (null, null, TryStaff);
                        var (first, last) = SplitName(mode.RiderFirstName, mode.RiderLastName, target.DisplayName);
                        var sigId = await _waivers.SignRegistrant(tenantId, waiverId, ip, req.SignatureDataUrl!,
                            signerEmail: null, signerName: isMinor ? guardianName : $"{first} {last}".Trim(),
                            attendeeFirstName: first, attendeeLastName: last, attendeeBirthdate: mode.RiderBirthdate,
                            signedByParent: isMinor, parentName: isMinor ? guardianName : null, parentPhone: isMinor ? guardianPhone : null);
                        var (_, still) = await GateWaiverLinking.LinkAndCheck(_gate, tenantId, target, sigId, waiverId);
                        if (still is not null) return (null, null, TryStaff);
                        signatureId = sigId;
                    }
                }
                if (push.WantPhoto && RiderPhotoRules.DecodePhoto(req.PhotoDataUrl) is { } img)
                {
                    if (push.EventTicketPurchaseId is Guid tid && await _tickets.GetById(tid, tenantId) is { } ticket)
                        photoId = (await _photos.RecordForTicket(ticket, tenantId, img.Bytes, img.Extension, null)).Id;
                    else if (push.SeasonPassPurchaseId is Guid pid && await _passes.GetPurchase(pid) is { } pass && pass.TenantId == tenantId)
                        photoId = (await _photos.RecordForPass(pass, tenantId, img.Bytes, img.Extension, null)).Id;
                    else
                        return (signatureId, null, TryStaff);
                }
                return (signatureId, photoId, null);
            }

            if (push.TargetKind == "kiosk_signup" && push.KioskSignupId is Guid sid
                && await _kiosks.GetSignup(sid, tenantId) is { } s)
            {
                var riderAccount = s.UserId is Guid uid ? BuyerAccounts.ForTenant(await _users.GetById(uid), tenantId) : null;
                var dep = s.DependentId is Guid did ? await _dependents.GetById(did, tenantId) : null;
                var guardian = dep is not null ? BuyerAccounts.ForTenant(await _users.GetById(dep.GuardianUserId), tenantId) : null;
                var isMinor = push.SignMode == "guardian" || s.IsMinor;
                if (push.WantWaiver && push.WaiverId is Guid waiverId)
                {
                    (Guid? Id, string? Error) signed;
                    if (riderAccount is not null)
                        signed = await AccountWaiverSigning.SignForAccount(_waivers, _gate, tenantId, _tenantContext.Tenant.Timezone,
                            riderAccount, waiverId, ip, req.SignatureDataUrl!, isMinor ? guardianName : null, isMinor ? guardianPhone : null);
                    else if (dep is not null && GuardianWaiverRule.ToDateOnly(dep.Birthdate) is { } bd)
                        signed = await AccountWaiverSigning.SignForDependent(_waivers, tenantId, waiverId, ip, req.SignatureDataUrl!,
                            dep.FirstName, dep.LastName, bd, guardianName, guardianPhone, guardian?.Email);
                    else
                        return (null, null, TryStaff);
                    if (signed.Error is not null) return (null, null, signed.Error);
                    signatureId = signed.Id;
                }
                if (push.WantPhoto && RiderPhotoRules.DecodePhoto(req.PhotoDataUrl) is { } img)
                {
                    RiderKeying? keying = dep is not null && guardian is not null ? TicketRiderKeys.ForDependent(guardian, dep.Id)
                        : riderAccount is not null ? TicketRiderKeys.ForRiderAccount(riderAccount) : null;
                    if (keying is null) return (signatureId, null, TryStaff);
                    var key = RiderPhotoRules.KeyFor(keying, null, s.FirstName, s.LastName, "kiosk", s.Id);
                    photoId = (await _photos.RecordForKey(tenantId, key, img.Bytes, img.Extension, null)).Id;
                }
                return (signatureId, photoId, null);
            }
            return (null, null, TryStaff);
        }

        private static string FirstWord(string? name) =>
            (name ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Rider";

        private static (string First, string Last) SplitName(string? first, string? last, string displayName)
        {
            if (first is not null && last is not null) return (first, last);
            var parts = displayName.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            return (first ?? (parts.Length > 0 ? parts[0] : "Rider"), last ?? (parts.Length > 1 ? parts[1] : string.Empty));
        }
    }
}
