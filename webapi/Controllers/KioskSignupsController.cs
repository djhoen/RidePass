using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Data.KioskData;
using Services.Repositories.Interfaces;
using Services.Riders;
using Services.Waivers;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Counter;
using webapi.Controllers.API.Data.Kiosk;
using webapi.Helpers;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    /// <summary>
    /// Kiosk sign-ups on the gate screens (Script0310): today's, newest first, for the Gate Sale (to pick one
    /// as the customer) and Scan Tickets. Either gate permission may use them (sales.counter or sales.redeem).
    /// A sign-up whose email matched an existing account waits here until a cashier checks ID and confirms:
    /// only then are its signature, photo and dependent attached to that account.
    /// </summary>
    [ApiController]
    [Route("api/KioskSignups")]
    [Authorize]
    public class KioskSignupsController : ControllerBase
    {
        private readonly IKioskRepository _kiosks;
        private readonly IAuthorizationService _authz;
        private readonly ICounterRiderProfiles _profiles;
        private readonly IKioskSignupWriter _writer;
        private readonly IRiderPhotos _photos;
        private readonly IUserRepository _users;
        private readonly IRiderDependentRepository _dependents;
        private readonly IWaiverRepository _waivers;
        private readonly Services.Audit.IAuditLogger _audit;
        private readonly ITenantContext _tenantContext;

        public KioskSignupsController(IKioskRepository kiosks, IAuthorizationService authz, ICounterRiderProfiles profiles,
            IKioskSignupWriter writer, IRiderPhotos photos, IUserRepository users, IRiderDependentRepository dependents,
            IWaiverRepository waivers, Services.Audit.IAuditLogger audit, ITenantContext tenantContext)
        {
            _kiosks = kiosks;
            _authz = authz;
            _profiles = profiles;
            _writer = writer;
            _photos = photos;
            _users = users;
            _dependents = dependents;
            _waivers = waivers;
            _audit = audit;
            _tenantContext = tenantContext;
        }

        private async Task<bool> MayUse() =>
            (await _authz.AuthorizeAsync(User, TenantPermissions.Policy.SalesCounter)).Succeeded
            || (await _authz.AuthorizeAsync(User, TenantPermissions.Policy.SalesRedeem)).Succeeded;

        private Guid? StaffId() => Guid.TryParse(User.FindFirst("UserId")?.Value, out var u) ? u : null;

        [HttpGet]
        public async Task<IActionResult> Today()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!await MayUse()) return Forbid();
            var tenantId = _tenantContext.TenantId;
            var tz = _tenantContext.Tenant.Timezone;
            var today = TenantClock.LocalDate(tz, DateTime.UtcNow);
            // Today in the venue's own time zone (DST-safe day bounds).
            var (sinceUtc, _) = TenantClock.DayRangeUtc(tz, today);
            var rows = await _kiosks.ListSignups(tenantId, sinceUtc, 100);
            var items = new List<KioskSignupItem>(rows.Count);
            foreach (var r in rows)
            {
                var bd = GuardianWaiverRule.ToDateOnly(r.Birthdate);
                items.Add(new KioskSignupItem
                {
                    Id = r.Id,
                    FirstName = r.FirstName,
                    LastName = r.LastName,
                    Birthdate = bd,
                    Age = bd is { } b ? GuardianWaiverRule.AgeOn(b, today) : null,
                    IsMinor = bd is { } m ? CounterRiderRules.IsMinor(m, today) : r.IsMinor,
                    RiderUserId = r.UserId,
                    RiderEmail = r.RiderEmail,
                    DependentId = r.DependentId,
                    GuardianUserId = r.GuardianUserId,
                    GuardianName = r.GuardianUserId is not null
                        ? $"{r.GuardianAccountFirstName} {r.GuardianAccountLastName}".Trim()
                        : r.GuardianFirstName is null ? null : $"{r.GuardianFirstName} {r.GuardianLastName}".Trim(),
                    GuardianEmail = r.GuardianAccountEmail ?? r.GuardianEmail,
                    WaiverSigned = r.SignatureId is not null || r.HeldSignatureDataUrl is not null,
                    PhotoId = r.PhotoId,
                    PhotoTakenAtUtc = r.PhotoTakenAt is { } p ? DateTime.SpecifyKind(p, DateTimeKind.Utc) : null,
                    KioskName = r.KioskName,
                    CreatedAtUtc = DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc),
                    HandledAtUtc = r.HandledAt is { } h ? DateTime.SpecifyKind(h, DateTimeKind.Utc) : null,
                    DismissedAtUtc = r.DismissedAt is { } x ? DateTime.SpecifyKind(x, DateTimeKind.Utc) : null,
                    MatchPending = r.MatchPending,
                    MatchedAccountName = r.MatchedUserId is null ? null : $"{r.MatchedFirstName} {r.MatchedLastName}".Trim(),
                    MatchedGuardianName = r.MatchedGuardianUserId is null ? null : $"{r.MatchedGuardianFirstName} {r.MatchedGuardianLastName}".Trim(),
                    MatchedHasPhoto = r.MatchPending && r.DismissedAt is null && await MatchedPhotoOnFile(r, tenantId),
                    TypedEmail = r.MatchPending ? r.Email : null,
                    TypedIdBirthdate = !r.MatchPending ? null
                        : GuardianWaiverRule.ToDateOnly(r.MatchedGuardianUserId is not null ? r.GuardianBirthdate : r.Birthdate),
                    MatchedIdBirthdate = !r.MatchPending ? null
                        : GuardianWaiverRule.ToDateOnly(r.MatchedGuardianUserId is not null ? r.MatchedGuardianBirthdate : r.MatchedBirthdate),
                });
            }
            return new ApiResponses().OkResult(items);
        }

        /// <summary>
        /// Attach a held sign-up to the existing account(s) it matched, after the cashier checked ID: the same
        /// profile rules as the kiosk and the Gate Sale (rider accounts only, empty fields filled, dependent
        /// under the guardian), the held signature through the venue signing rules (only if that waiver is
        /// still the active one), and the held photo (a photo already on file is kept unless ReplacePhoto).
        /// </summary>
        [HttpPost("{id:guid}/Confirm")]
        public async Task<IActionResult> Confirm(Guid id, [FromBody] KioskConfirmRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!await MayUse()) return Forbid();
            if (!req.IdChecked) return new ApiResponses().BadRequestResult("Check their photo ID against the account first.");
            var tenantId = _tenantContext.TenantId;
            var s = await _kiosks.GetSignup(id, tenantId);
            if (s is null) return new ApiResponses().NotFoundResult("Sign-up not found.");
            if (s.ConfirmedAt is not null) return new ApiResponses().ConflictResult("Already confirmed.");
            if (!s.MatchPending || s.DismissedAt is not null)
                return new ApiResponses().BadRequestResult("This sign-up isn't waiting for an ID check.");

            var input = SnapshotInput(s);
            var (plan, planError) = await _profiles.Plan(input, null);
            if (planError is not null) return new ApiResponses().BadRequestResult(planError);
            var active = await _waivers.GetActive(tenantId);
            var waiverCurrent = s.HeldWaiverId is Guid hw && active is not null && active.Id == hw
                && (active.ExpiresAt is null || active.ExpiresAt > DateTime.UtcNow);
            var guardianName = s.GuardianFirstName is null ? null : $"{s.GuardianFirstName} {s.GuardianLastName}".Trim();
            // Everything the attach would refuse, before any profile is written.
            if (KioskSignupWriter.Validate(plan!, waiverCurrent ? s.HeldWaiverId : null, waiverCurrent ? s.HeldSignatureDataUrl : null,
                    guardianName, s.GuardianPhone) is { } attachError)
                return new ApiResponses().BadRequestResult(attachError);

            // Claimed once (a guarded UPDATE): two staff confirming at once can't both attach; the second is told.
            if (!await _kiosks.ClaimForConfirm(id, tenantId))
            {
                var now = await _kiosks.GetSignup(id, tenantId);
                return new ApiResponses().ConflictResult(now?.ConfirmedAt is not null
                    ? "Already confirmed." : "Someone else is confirming this sign-up right now.");
            }
            OtherRiderSaved? saved = null;
            KioskAttachResult attached;
            try
            {
                try
                {
                    saved = (await _profiles.Save(new[] { plan! }, StaffId()))[0];
                }
                catch (InvalidOperationException ex)
                {
                    await _kiosks.ReleaseConfirm(id, tenantId);
                    return new ApiResponses().BadRequestResult(ex.Message);
                }

                (byte[] Bytes, string Extension)? photo = null;
                if (s.PhotoId is Guid heldPhotoId && await _photos.Open(heldPhotoId, tenantId) is { } img)
                    photo = (img.Bytes, img.ContentType == "image/png" ? ".png" : ".jpg");

                attached = await _writer.Attach(tenantId, _tenantContext.Tenant.Timezone, plan!, saved,
                    waiverCurrent ? s.HeldWaiverId : null, waiverCurrent ? s.HeldSignatureDataUrl : null,
                    HttpContext.Connection.RemoteIpAddress?.ToString(), guardianName, s.GuardianPhone, s.GuardianEmail,
                    photo, req.ReplacePhoto, StaffId());
                if (attached.Error is not null)
                {
                    await _profiles.Discard(saved);
                    await _kiosks.ReleaseConfirm(id, tenantId);
                    return new ApiResponses().BadRequestResult(attached.Error);
                }
                if (!await _kiosks.Confirm(id, tenantId, saved.RiderUserId, saved.DependentId, saved.GuardianUserId,
                        attached.SignatureId, attached.PhotoId, StaffId()))
                    return new ApiResponses().ConflictResult("Already confirmed.");
            }
            catch
            {
                // Give the sign-up back for another try, and remove any profile this attempt just created.
                if (saved is not null) await _profiles.Discard(saved);
                await _kiosks.ReleaseConfirm(id, tenantId);
                throw;
            }

            // The held photo has been copied to the rider's own key (or the one on file was kept): drop it.
            if (s.PhotoId is Guid held) await _photos.DeletePhoto(held, tenantId);
            await _audit.Log("kiosk.signup_confirmed", $"Kiosk sign-up for {s.FirstName} {s.LastName} confirmed after an ID check",
                targetKind: "kiosk_signup", targetId: id, tenantId: tenantId,
                metadata: new { saved.RiderUserId, saved.DependentId, saved.GuardianUserId, attached.SignatureId,
                    attached.PhotoId, kept = attached.KeptExistingPhoto, replace = req.ReplacePhoto });
            return new ApiResponses().OkResult(new KioskConfirmResponse
            {
                WaiverAttached = attached.SignatureId is not null,
                WaiverChanged = s.HeldWaiverId is not null && !waiverCurrent,
                PhotoAttached = attached.PhotoId is not null && !attached.KeptExistingPhoto,
                KeptExistingPhoto = attached.KeptExistingPhoto,
            });
        }

        [HttpPost("{id:guid}/Dismiss")]
        public async Task<IActionResult> Dismiss(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!await MayUse()) return Forbid();
            var s = await _kiosks.GetSignup(id, _tenantContext.TenantId);
            if (s is null) return new ApiResponses().NotFoundResult("Sign-up not found.");
            // A held sign-up's photo and signature image belong to nobody yet: dismissing deletes them.
            if (s.MatchPending && s.PhotoId is Guid held)
            {
                await _photos.DeletePhoto(held, _tenantContext.TenantId);
                await _kiosks.SetPhoto(id, _tenantContext.TenantId, null);
            }
            await _kiosks.Dismiss(id, _tenantContext.TenantId, StaffId());
            return new ApiResponses().OkResult();
        }

        /// <summary>The held sign-up as the profile check reads it (exactly what was typed at the kiosk).</summary>
        private static CounterOtherRiderInput SnapshotInput(KioskSignup s) => new()
        {
            FirstName = s.FirstName,
            LastName = s.LastName,
            Birthdate = s.Birthdate,
            Email = s.Email,
            Phone = s.Phone,
            GuardianFirstName = s.IsMinor ? s.GuardianFirstName : null,
            GuardianLastName = s.IsMinor ? s.GuardianLastName : null,
            GuardianEmail = s.IsMinor ? s.GuardianEmail : null,
            GuardianPhone = s.IsMinor ? s.GuardianPhone : null,
            GuardianBirthdate = s.IsMinor ? s.GuardianBirthdate : null,
            EmergencyContactName = s.EmergencyContactName,
            EmergencyContactPhone = s.EmergencyContactPhone,
        };

        /// <summary>Whether the matched rider already has a photo on file where the gate looks (their account,
        /// or their dependent row under the matched guardian), so staff are offered "Replace photo on file".</summary>
        private async Task<bool> MatchedPhotoOnFile(KioskSignupRow r, Guid tenantId)
        {
            RiderKeying? keying = null;
            if (!r.IsMinor && r.MatchedUserId is Guid uid
                && BuyerAccounts.ForTenant(await _users.GetById(uid), tenantId) is { } account)
                keying = TicketRiderKeys.ForRiderAccount(account);
            else if (r.IsMinor && r.MatchedGuardianUserId is Guid gid
                     && BuyerAccounts.ForTenant(await _users.GetById(gid), tenantId) is { } guardian
                     && await _dependents.FindByKey(tenantId, gid, IdCheckRules.Normalize($"{r.FirstName} {r.LastName}")) is { } dep)
                keying = TicketRiderKeys.ForDependent(guardian, dep.Id);
            if (keying is null) return false;
            var key = RiderPhotoRules.KeyFor(keying, null, r.FirstName, r.LastName, "kiosk", r.Id);
            return await _photos.Find(tenantId, key) is not null;
        }
    }
}
