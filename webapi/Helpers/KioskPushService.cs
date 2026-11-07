using Services.Helpers;
using Services.Repositories.Data.KioskData;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Data.RiderData;
using Services.Repositories.Data.UserData;
using Services.Repositories.Interfaces;
using Services.Riders;
using Services.Waivers;
using webapi.Controllers.API.Data.Kiosk;
using webapi.Multitenancy;

namespace webapi.Helpers
{
    /// <summary>
    /// Waiver and photo steps pushed to a kiosk for one rider (Script0311). Who signs is decided ONCE, when the
    /// request is built, by the function the signing paths themselves use (SignRequestMode.ForTarget for a
    /// ticket / pass, AccountWaiverSigning.SignModeFor for a kiosk sign-up's profile), stored on the request
    /// (sign_mode), shown by the kiosk, and enforced at completion: if it no longer holds (a birthday crossed)
    /// the request ends and staff send it again, so a guardian's signature is never stored as the rider's own.
    /// Completion is idempotent: everything is checked before the first write, the writes run in order
    /// (signature, link, photo), and each result is recorded on the request right after it is written, so a
    /// retry reuses it instead of signing or saving again.
    /// </summary>
    public interface IKioskPushService
    {
        /// <summary>The request to store for this target and these steps, or why it can't be sent.</summary>
        Task<(KioskPush? Push, string? Error)> Build(Guid tenantId, CreateKioskPushRequest req, Guid? staffUserId);

        /// <summary>Checks what the kiosk sent before anything is claimed (so a typo can be fixed and retried).</summary>
        string? ValidateCompletion(KioskPush push, KioskPushCompleteRequest req);

        /// <summary>Signs / saves for a request claimed by this attempt (push.ClaimId).</summary>
        Task<KioskPushOutcome> Complete(KioskPush push, KioskPushCompleteRequest req, string? ip);
    }

    public class KioskPushService : IKioskPushService
    {
        public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
        public const string TryStaff = "This couldn't be saved here. Please ask staff to send it again.";
        public const string WaiverChanged = "The waiver changed. Please ask staff to send it again.";

        private readonly IWaiverCheckInGate _gate;
        private readonly IWaiverRepository _waivers;
        private readonly IEventTicketPurchaseRepository _tickets;
        private readonly IEventRepository _events;
        private readonly ISeasonPassRepository _passes;
        private readonly IKioskRepository _kiosks;
        private readonly IUserRepository _users;
        private readonly IRiderDependentRepository _dependents;
        private readonly IRiderPhotos _photos;
        private readonly ITenantContext _tenantContext;

        public KioskPushService(IWaiverCheckInGate gate, IWaiverRepository waivers, IEventTicketPurchaseRepository tickets, IEventRepository events,
            ISeasonPassRepository passes, IKioskRepository kiosks, IUserRepository users, IRiderDependentRepository dependents,
            IRiderPhotos photos, ITenantContext tenantContext)
        {
            _gate = gate;
            _waivers = waivers;
            _tickets = tickets;
            _events = events;
            _passes = passes;
            _kiosks = kiosks;
            _users = users;
            _dependents = dependents;
            _photos = photos;
            _tenantContext = tenantContext;
        }

        /// <summary>A waiver can be signed: it exists for the tenant and has not expired.</summary>
        public static bool WaiverUsable(TenantWaiver? w, DateTime utcNow) =>
            w is not null && (w.ExpiresAt is null || DateTime.SpecifyKind(w.ExpiresAt.Value, DateTimeKind.Utc) > utcNow);

        /// <summary>Two tickets of one rider in one order: same event, same buyer.</summary>
        public static bool SameOrderRider(EventTicketPurchase a, Guid? aEventId, EventTicketPurchase b, Guid? bEventId) =>
            aEventId is not null && aEventId == bEventId && a.TenantId == b.TenantId
            && (a.PurchaserUserId is Guid au ? au == b.PurchaserUserId
                : string.Equals(a.PurchaserEmail?.Trim(), b.PurchaserEmail?.Trim(), StringComparison.OrdinalIgnoreCase));

        private string? Timezone => _tenantContext.Tenant.Timezone;

        private async Task<TenantWaiver?> ActiveWaiver(Guid tenantId)
        {
            var w = await _waivers.GetActive(tenantId);
            return WaiverUsable(w, DateTime.UtcNow) ? w : null;
        }

        /// <summary>A kiosk sign-up's profile: the rider's own account, or a dependent and their guardian.</summary>
        private async Task<(User? Rider, RiderDependent? Dependent, User? Guardian)> Profile(KioskSignup s, Guid tenantId)
        {
            var rider = s.UserId is Guid uid ? BuyerAccounts.ForTenant(await _users.GetById(uid), tenantId) : null;
            var dep = s.DependentId is Guid did ? await _dependents.GetById(did, tenantId) : null;
            var guardian = dep is not null ? BuyerAccounts.ForTenant(await _users.GetById(dep.GuardianUserId), tenantId) : null;
            return (rider, dep, guardian);
        }

        private static RiderKeying? PhotoKeying(User? rider, RiderDependent? dep, User? guardian) =>
            dep is not null ? (guardian is not null ? TicketRiderKeys.ForDependent(guardian, dep.Id) : null)
            : rider is not null ? TicketRiderKeys.ForRiderAccount(rider) : null;

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
                var isTicket = req.TargetKind == "ticket";
                var target = await _gate.ResolveSignTarget(tenantId,
                    isTicket ? pid : null, isTicket ? null : pid, isTicket ? null : req.EventId);
                if (target is null) return (null, isTicket ? "Ticket not found." : "Pass not found.");
                var d = target.Decision;
                push.EventTicketPurchaseId = target.TicketId;
                push.SeasonPassPurchaseId = target.PassPurchaseId;
                // A pass is judged for the event the request names: only ever one of this tenant's events.
                if (!isTicket && req.EventId is Guid passEvent && await _events.GetById(passEvent, tenantId) is null)
                    return (null, "Event not found.");
                push.EventId = isTicket ? target.EventId : req.EventId;
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
                    if (!WaiverUsable(await _waivers.GetById(d.RequiredWaiverId.Value, tenantId), DateTime.UtcNow))
                        return (null, "The waiver this event needs can't be signed right now (it was removed or has expired).");
                    push.SignMode = mode.SignAs;
                    push.WaiverId = d.RequiredWaiverId;
                }
                if (req.Photo)
                {
                    if (isTicket && req.PhotoPurchaseId is Guid photoId && photoId != pid)
                    {
                        // A registrant's photo is checked on their rider ticket, which may not be the ticket
                        // the waiver is fixed on: the same rider's, in the same order.
                        var photoTarget = await _gate.ResolveSignTarget(tenantId, photoId, null, null);
                        var main = await _tickets.GetById(pid, tenantId);
                        var photoTicket = await _tickets.GetById(photoId, tenantId);
                        if (photoTarget is null || main is null || photoTicket is null)
                            return (null, "Ticket not found.");
                        if (!SameOrderRider(main, target.EventId, photoTicket, photoTarget.EventId))
                            return (null, "The photo ticket isn't part of this rider's order.");
                        if (!photoTarget.Decision.RiderAudience) return (null, "Photos are for riders; this is a spectator ticket.");
                        push.PhotoTicketPurchaseId = photoId;
                    }
                    else if (isTicket && !d.RiderAudience)
                        return (null, "Photos are for riders; this is a spectator ticket.");
                }
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
                var (rider, dep, guardian) = await Profile(s, tenantId);
                if (rider is null && dep is null) return (null, "That sign-up's profile is gone.");
                push.KioskSignupId = s.Id;
                push.RiderFirstName = s.FirstName;
                if (req.Waiver)
                {
                    var active = await ActiveWaiver(tenantId);
                    if (active is null) return (null, "There is no active waiver to sign.");
                    // The signing helpers' own rule (account: ID-verified birthdate first; dependent: theirs),
                    // on the tenant's date, so Build, the kiosk and Complete can never disagree.
                    var mode = AccountWaiverSigning.SignModeFor(rider, dep, Timezone, DateTime.UtcNow);
                    if (mode is null)
                        return (null, $"{s.FirstName}'s birthdate isn't known, so the kiosk can't tell who signs.");
                    push.SignMode = mode;
                    push.WaiverId = active.Id;
                }
                if (req.Photo && PhotoKeying(rider, dep, guardian) is null)
                    return (null, "This profile has no account to keep a photo on.");
                return (push, null);
            }
            return (null, "Unknown target.");
        }

        public string? ValidateCompletion(KioskPush push, KioskPushCompleteRequest req)
        {
            if (push.WantWaiver && push.ResultSignatureId is null)
            {
                if (req.WaiverId != push.WaiverId) return WaiverChanged;
                if (!req.AgreedToWaiver) return "Read the waiver and tick that you agree before signing.";
                if (!SignatureImages.IsValidPngDataUrl(req.SignatureDataUrl)) return "Sign the waiver with your finger in the box.";
                if (push.SignMode == AccountWaiverSigning.Guardian
                    && (string.IsNullOrWhiteSpace(req.GuardianName) || !RiderAccounts.IsValidPhone(req.GuardianPhone)))
                    return AccountWaiverSigning.MinorNeedsGuardian;
            }
            if (push.WantPhoto && push.ResultPhotoId is null && RiderPhotoRules.DecodePhoto(req.PhotoDataUrl) is null)
                return "Take the photo to finish.";
            return null;
        }

        public async Task<KioskPushOutcome> Complete(KioskPush push, KioskPushCompleteRequest req, string? ip)
        {
            if (push.ClaimId is not Guid claimId) return KioskPushOutcome.Retry(TryStaff);
            if (push.WantWaiver && push.WaiverId is null)
                return KioskPushOutcome.End(KioskPush.EndWaiverChanged, WaiverChanged); // the waiver was deleted
            return push.TargetKind switch
            {
                "ticket" or "season_pass" => await CompleteForTarget(push, claimId, req, ip),
                "kiosk_signup" => await CompleteForSignup(push, claimId, req, ip),
                _ => KioskPushOutcome.Retry(TryStaff),
            };
        }

        private async Task<KioskPushOutcome> CompleteForTarget(KioskPush push, Guid claimId, KioskPushCompleteRequest req, string? ip)
        {
            var tenantId = push.TenantId;
            var target = await _gate.ResolveSignTarget(tenantId, push.EventTicketPurchaseId, push.SeasonPassPurchaseId, push.EventId);
            if (target is null) return KioskPushOutcome.Retry(TryStaff);

            // ── Everything checked before the first write ──
            var signNow = push.WantWaiver && push.ResultSignatureId is null;
            if (push.WantWaiver && !WaiverUsable(await _waivers.GetById(push.WaiverId!.Value, tenantId), DateTime.UtcNow))
                return KioskPushOutcome.End(KioskPush.EndWaiverChanged, WaiverChanged);
            if (signNow)
            {
                var d = target.Decision;
                // Still blocked for a different document: the event's waiver changed since it was sent.
                if (d.BlockReason is not null && !d.Satisfied && d.RequiredWaiverId is Guid rw && rw != push.WaiverId)
                    return KioskPushOutcome.End(KioskPush.EndWaiverChanged, WaiverChanged);
                // The mode it was sent (and shown) with must still be the gate's.
                if (SignRequestMode.ForTarget(target).SignAs != push.SignMode)
                    return KioskPushOutcome.End(KioskPush.EndModeChanged, AccountWaiverSigning.ModeChanged);
            }
            EventTicketPurchase? photoTicket = null;
            SeasonPassPurchase? photoPass = null;
            var photoNow = push.WantPhoto && push.ResultPhotoId is null;
            var image = photoNow ? RiderPhotoRules.DecodePhoto(req.PhotoDataUrl) : null;
            if (photoNow)
            {
                if (image is null) return KioskPushOutcome.Retry("Take the photo to finish.");
                if ((push.PhotoTicketPurchaseId ?? push.EventTicketPurchaseId) is Guid tid)
                    photoTicket = await _tickets.GetById(tid, tenantId);
                else if (push.SeasonPassPurchaseId is Guid pid && await _passes.GetPurchase(pid) is { } pass && pass.TenantId == tenantId)
                    photoPass = pass;
                if (photoTicket is null && photoPass is null) return KioskPushOutcome.Retry(TryStaff);
            }

            // ── Writes, in order; each result recorded on the request as soon as it exists ──
            var signatureId = push.ResultSignatureId;
            if (signNow)
            {
                var guardian = push.SignMode == AccountWaiverSigning.Guardian;
                var mode = SignRequestMode.ForTarget(target);
                var (first, last) = SplitName(mode.RiderFirstName, mode.RiderLastName, target.DisplayName);
                // The signing-link rules: a minor's is their parent's or guardian's (named, with a phone), and
                // the signature names the rider; an adult signs as themselves.
                signatureId = await _waivers.SignRegistrant(tenantId, push.WaiverId!.Value, ip, req.SignatureDataUrl!,
                    signerEmail: null, signerName: guardian ? req.GuardianName?.Trim() : $"{first} {last}".Trim(),
                    attendeeFirstName: first, attendeeLastName: last, attendeeBirthdate: mode.RiderBirthdate,
                    signedByParent: guardian, parentName: guardian ? req.GuardianName?.Trim() : null,
                    parentPhone: guardian ? req.GuardianPhone?.Trim() : null);
                await _kiosks.RecordPushSignature(push.Id, tenantId, claimId, signatureId.Value);
            }
            if (push.WantWaiver)
            {
                if (signatureId is not Guid sigId) return KioskPushOutcome.Retry(TryStaff);
                // Linking fills only what is missing or failing, so a retry over a linked signature is a no-op.
                var (_, still) = await GateWaiverLinking.LinkAndCheck(_gate, tenantId, target, sigId, push.WaiverId!.Value);
                if (still is not null) return KioskPushOutcome.Retry(TryStaff) with { SignatureId = sigId };
            }
            var photoId = push.ResultPhotoId;
            if (photoNow)
            {
                photoId = photoTicket is not null
                    ? (await _photos.RecordForTicket(photoTicket, tenantId, image!.Value.Bytes, image.Value.Extension, null)).Id
                    : (await _photos.RecordForPass(photoPass!, tenantId, image!.Value.Bytes, image.Value.Extension, null)).Id;
                await _kiosks.RecordPushPhoto(push.Id, tenantId, claimId, photoId.Value);
            }
            return new KioskPushOutcome(signatureId, photoId, null, null);
        }

        private async Task<KioskPushOutcome> CompleteForSignup(KioskPush push, Guid claimId, KioskPushCompleteRequest req, string? ip)
        {
            var tenantId = push.TenantId;
            if (push.KioskSignupId is not Guid sid || await _kiosks.GetSignup(sid, tenantId) is not { } s)
                return KioskPushOutcome.Retry(TryStaff);
            var (rider, dep, guardian) = await Profile(s, tenantId);
            if (rider is null && dep is null) return KioskPushOutcome.Retry(TryStaff);

            // ── Everything checked before the first write ──
            var signNow = push.WantWaiver && push.ResultSignatureId is null;
            if (push.WantWaiver && (await ActiveWaiver(tenantId))?.Id != push.WaiverId)
                return KioskPushOutcome.End(KioskPush.EndWaiverChanged, WaiverChanged);
            DateOnly? depBirthdate = dep is not null ? GuardianWaiverRule.ToDateOnly(dep.Birthdate) : null;
            if (signNow)
            {
                // The same function Build used: a birthday crossed since it was sent ends the request.
                if (AccountWaiverSigning.SignModeFor(rider, dep, Timezone, DateTime.UtcNow) != push.SignMode)
                    return KioskPushOutcome.End(KioskPush.EndModeChanged, AccountWaiverSigning.ModeChanged);
                if (rider is null && depBirthdate is null) return KioskPushOutcome.Retry(TryStaff);
            }
            var photoNow = push.WantPhoto && push.ResultPhotoId is null;
            var image = photoNow ? RiderPhotoRules.DecodePhoto(req.PhotoDataUrl) : null;
            var keying = PhotoKeying(rider, dep, guardian);
            if (photoNow && (image is null || keying is null)) return KioskPushOutcome.Retry(image is null ? "Take the photo to finish." : TryStaff);

            // ── Writes, in order ──
            var signatureId = push.ResultSignatureId;
            if (signNow)
            {
                var asGuardian = push.SignMode == AccountWaiverSigning.Guardian;
                string? gName = asGuardian ? req.GuardianName?.Trim() : null, gPhone = asGuardian ? req.GuardianPhone?.Trim() : null;
                var waiverId = push.WaiverId!.Value;
                (Guid? Id, string? Error) signed = rider is not null
                    // Their own account: a guardian's signature is stored as signed by the guardian.
                    ? await AccountWaiverSigning.SignForAccount(_waivers, _gate, tenantId, Timezone, rider, waiverId, ip,
                        req.SignatureDataUrl!, gName, gPhone, expectMinor: asGuardian)
                    : asGuardian
                        ? await AccountWaiverSigning.SignForDependent(_waivers, tenantId, waiverId, ip, req.SignatureDataUrl!,
                            dep!.FirstName, dep.LastName, depBirthdate!.Value, gName, gPhone, guardian?.Email)
                        // An adult dependent signs as themselves, never through the guardian path.
                        : await AccountWaiverSigning.SignAsAdultRegistrant(_waivers, tenantId, waiverId, ip, req.SignatureDataUrl!,
                            dep!.FirstName, dep.LastName, depBirthdate!.Value, email: null);
                if (signed.Error == AccountWaiverSigning.ModeChanged)
                    return KioskPushOutcome.End(KioskPush.EndModeChanged, AccountWaiverSigning.ModeChanged);
                if (signed.Error is not null || signed.Id is null) return KioskPushOutcome.Retry(signed.Error ?? TryStaff);
                signatureId = signed.Id;
                await _kiosks.RecordPushSignature(push.Id, tenantId, claimId, signatureId.Value);
            }
            if (push.WantWaiver && signatureId is null) return KioskPushOutcome.Retry(TryStaff);
            var photoId = push.ResultPhotoId;
            if (photoNow)
            {
                var key = RiderPhotoRules.KeyFor(keying!, null, s.FirstName, s.LastName, "kiosk", s.Id);
                photoId = (await _photos.RecordForKey(tenantId, key, image!.Value.Bytes, image.Value.Extension, null)).Id;
                await _kiosks.RecordPushPhoto(push.Id, tenantId, claimId, photoId.Value);
            }
            return new KioskPushOutcome(signatureId, photoId, null, null);
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
