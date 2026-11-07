using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Services.Helpers;
using Services.Repositories.Data.KioskData;
using Services.Repositories.Interfaces;
using Services.Riders;
using webapi.Controllers.API.Data.Counter;
using webapi.Controllers.API.Data.Kiosk;
using webapi.Helpers;
using webapi.Multitenancy;
using webapi.Security;

namespace webapi.Controllers
{
    /// <summary>
    /// The front-desk kiosk API (Script0310). Authenticated by the kiosk's own secret (X-Kiosk-Token), never
    /// a staff session, and able to do exactly three things: pair, read the venue's kiosk settings (waiver
    /// text, photo rule), and create a sign-up. It reads no customer data and its responses never say
    /// anything about an existing account (no names, no "welcome back"), so a kiosk can't be used to probe
    /// who has an account. Rate-limited per device.
    /// </summary>
    [ApiController]
    [Route("api/Kiosk")]
    [AllowAnonymous]
    public class KioskController : ControllerBase
    {
        private const string GenericFailure = "We couldn't finish your check-in here. Please see the front desk.";
        private const string StaffEmailRefused = "That email can't be used here. Please use a different email.";

        private readonly IKioskAuthenticator _auth;
        private readonly IKioskRepository _kiosks;
        private readonly ICounterRiderProfiles _profiles;
        private readonly IUserRepository _users;
        private readonly IWaiverRepository _waivers;
        private readonly Services.Waivers.IWaiverCheckInGate _waiverGate;
        private readonly ITenantRepository _tenants;
        private readonly IRiderPhotos _photos;
        private readonly IKioskSignupWriter _writer;
        private readonly Services.Audit.IAuditLogger _audit;
        private readonly ITenantContext _tenantContext;

        public KioskController(IKioskAuthenticator auth, IKioskRepository kiosks, ICounterRiderProfiles profiles,
            IUserRepository users, IWaiverRepository waivers, Services.Waivers.IWaiverCheckInGate waiverGate,
            ITenantRepository tenants, IRiderPhotos photos, Services.Audit.IAuditLogger audit, ITenantContext tenantContext,
            IKioskSignupWriter writer)
        {
            _auth = auth;
            _kiosks = kiosks;
            _profiles = profiles;
            _users = users;
            _waivers = waivers;
            _waiverGate = waiverGate;
            _tenants = tenants;
            _photos = photos;
            _writer = writer;
            _audit = audit;
            _tenantContext = tenantContext;
        }

        private IActionResult NotPaired() =>
            StatusCode(401, new { error = "This kiosk isn't paired (or was removed). Ask staff to pair it again." });

        /// <summary>Swaps a one-time pairing code for this tablet's secret. Public (the tablet has no session),
        /// tenant-resolved, rate-limited per address; a code works once and expires after 15 minutes.</summary>
        [HttpPost("Pair")]
        [EnableRateLimiting("kiosk-pair")]
        public async Task<IActionResult> Pair([FromBody] KioskPairRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var code = KioskCredentials.NormalizeCode(req.Code);
            if (code.Length != KioskCredentials.CodeLength)
                return new ApiResponses().BadRequestResult("Enter the 8-character pairing code shown on Admin > Kiosks.");
            var secret = KioskCredentials.NewSecret();
            var device = await _kiosks.RedeemPairingCode(_tenantContext.TenantId,
                KioskCredentials.HashPairingCode(_tenantContext.TenantId, code), KioskCredentials.Hash(secret));
            if (device is null)
                return new ApiResponses().BadRequestResult("That pairing code isn't valid or has expired. Ask staff for a new one.");
            await _audit.Log("kiosk.paired", $"Kiosk \"{device.Name}\" paired", targetKind: "kiosk_device",
                targetId: device.Id, tenantId: device.TenantId);
            return new ApiResponses().OkResult(new KioskPairResponse { Token = secret, KioskName = device.Name });
        }

        /// <summary>The venue's kiosk settings: its name, the active rider waiver's text and the photo rule.</summary>
        [HttpGet("Config")]
        [EnableRateLimiting("kiosk")]
        public async Task<IActionResult> Config()
        {
            var device = await _auth.Authenticate(Request);
            if (device is null) return NotPaired();
            var tenant = await _tenants.GetById(device.TenantId) ?? _tenantContext.Tenant;
            var waiver = await ActiveWaiver(device.TenantId);
            var mode = PhotoMode(tenant.RiderPhotoMode);
            return new ApiResponses().OkResult(new KioskConfigResponse
            {
                KioskName = device.Name,
                VenueName = tenant.DisplayName,
                WaiverId = waiver?.Id,
                WaiverTitle = waiver?.Title,
                WaiverBody = waiver?.Body,
                PhotoMode = mode,
                PhotoRequired = mode != IdVerificationWindow.ModeOff,
                RequireEmergencyContact = tenant.RequireEmergencyContact,
                Today = TenantClock.LocalDate(tenant.Timezone, DateTime.UtcNow),
            });
        }

        /// <summary>
        /// A walk-up's sign-up. A brand-new email: the profile is created now, with the signed waiver (their own,
        /// or a parent or guardian's for a minor) and the photo. An email that matches an existing account: held
        /// for a cashier to confirm after checking ID (nothing written to that account). The answer is the same.
        /// </summary>
        [HttpPost("Signup")]
        [EnableRateLimiting("kiosk")]
        public async Task<IActionResult> Signup([FromBody] KioskSignupRequest req)
        {
            var device = await _auth.Authenticate(Request);
            if (device is null) return NotPaired();
            var tenantId = device.TenantId;
            var tenant = await _tenants.GetById(tenantId) ?? _tenantContext.Tenant;
            var today = TenantClock.LocalDate(tenant.Timezone, DateTime.UtcNow);

            // ── What was typed, judged on its own (these messages reveal nothing about any account) ──
            var dob = req.Birthdate is { } b ? DateOnly.FromDateTime(b) : (DateOnly?)null;
            var typedMinor = dob is { } d && CounterRiderRules.IsMinor(d, today);
            var shape = new OtherRiderInput(req.FirstName, req.LastName, dob, req.Email, req.Phone,
                GuardianFirstName: typedMinor ? req.GuardianFirstName : null,
                GuardianLastName: typedMinor ? req.GuardianLastName : null,
                GuardianEmail: typedMinor ? req.GuardianEmail : null,
                GuardianPhone: typedMinor ? req.GuardianPhone : null);
            var invalid = CounterRiderRules.Validate(shape, today, existingProfile: false, payerEmail: null);
            if (invalid is not null) return new ApiResponses().BadRequestResult(invalid);
            var guardianDob = req.GuardianBirthdate is { } gb ? DateOnly.FromDateTime(gb) : (DateOnly?)null;
            var guardianName = typedMinor ? $"{req.GuardianFirstName!.Trim()} {req.GuardianLastName!.Trim()}" : null;
            if (typedMinor)
            {
                if (!RiderAccounts.IsValidPhone(req.GuardianPhone))
                    return new ApiResponses().BadRequestResult("Enter the parent or guardian's phone number.");
                var ageError = CounterRiderRules.GuardianAgeError("The parent or guardian", guardianDob, today);
                if (ageError is not null) return new ApiResponses().BadRequestResult(ageError);
            }
            else if (tenant.RequireEmergencyContact
                     && RiderAccounts.EmergencyContactError(req.EmergencyContactName, req.EmergencyContactPhone) is { } ecError)
            {
                return new ApiResponses().BadRequestResult(ecError);
            }

            // The waiver the kiosk showed must still be the one in force: a changed waiver means start again.
            var waiver = await ActiveWaiver(tenantId);
            if (waiver?.Id != req.WaiverId)
                return new ApiResponses().BadRequestResult("The waiver changed, please start again.");
            if (waiver is not null)
            {
                if (!req.AgreedToWaiver)
                    return new ApiResponses().BadRequestResult("Read the waiver and tick that you agree before signing.");
                if (!SignatureImages.IsValidPngDataUrl(req.SignatureDataUrl))
                    return new ApiResponses().BadRequestResult("Sign the waiver with your finger in the box.");
            }
            var mode = PhotoMode(tenant.RiderPhotoMode);
            (byte[] Bytes, string Extension)? photo = null;
            if (!string.IsNullOrWhiteSpace(req.PhotoDataUrl))
            {
                photo = RiderPhotoRules.DecodePhoto(req.PhotoDataUrl);
                if (photo is null) return new ApiResponses().BadRequestResult("That photo didn't come through. Please take it again.");
            }
            else if (mode != IdVerificationWindow.ModeOff)
            {
                return new ApiResponses().BadRequestResult("Take your photo to finish checking in.");
            }

            // ── The profile: the same rules as the Gate Sale (rider accounts only, never duplicated) ──
            var input = new CounterOtherRiderInput
            {
                FirstName = req.FirstName, LastName = req.LastName, Birthdate = req.Birthdate,
                Email = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email,
                Phone = req.Phone,
                GuardianFirstName = typedMinor ? req.GuardianFirstName : null,
                GuardianLastName = typedMinor ? req.GuardianLastName : null,
                GuardianEmail = typedMinor ? req.GuardianEmail : null,
                GuardianPhone = typedMinor ? req.GuardianPhone : null,
                GuardianBirthdate = typedMinor ? req.GuardianBirthdate : null,
                EmergencyContactName = req.EmergencyContactName,
                EmergencyContactPhone = req.EmergencyContactPhone,
            };
            var (plan, planError) = await _profiles.Plan(input, null);
            // Anything that depends on an existing account is answered without saying what it found.
            if (planError is not null)
                return new ApiResponses().BadRequestResult(planError == CounterRiderProfiles.StaffEmailMessage ? StaffEmailRefused : GenericFailure);
            if (plan!.IsMinor != typedMinor) return new ApiResponses().BadRequestResult(GenericFailure);
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();

            // ── An email that matches an existing account: held for staff, nothing written to that account ──
            // An unattended kiosk can't tell who is typing someone's email, so the sign-up waits at the gate
            // ("Matches an existing account: check ID"); the cashier attaches it after checking ID. What was
            // typed, the signature image and the photo (under "kiosk:{id}", no account's key) are kept on the
            // sign-up until then. The answer is the same as for a new account.
            if (plan.MatchedExistingRider || plan.MatchedExistingGuardian)
            {
                var pendingId = await _kiosks.CreateSignup(new KioskSignup
                {
                    TenantId = tenantId,
                    KioskDeviceId = device.Id,
                    FirstName = req.FirstName!.Trim(),
                    LastName = req.LastName!.Trim(),
                    Birthdate = req.Birthdate,
                    IsMinor = plan.IsMinor,
                    MatchPending = true,
                    MatchedUserId = plan.Rider?.ExistingId,
                    MatchedGuardianUserId = plan.IsMinor ? plan.Guardian?.ExistingId : null,
                    Email = input.Email?.Trim(),
                    Phone = string.IsNullOrWhiteSpace(req.Phone) ? null : req.Phone.Trim(),
                    GuardianFirstName = input.GuardianFirstName?.Trim(),
                    GuardianLastName = input.GuardianLastName?.Trim(),
                    GuardianEmail = input.GuardianEmail?.Trim(),
                    GuardianPhone = input.GuardianPhone?.Trim(),
                    GuardianBirthdate = input.GuardianBirthdate,
                    EmergencyContactName = string.IsNullOrWhiteSpace(req.EmergencyContactName) ? null : req.EmergencyContactName.Trim(),
                    EmergencyContactPhone = string.IsNullOrWhiteSpace(req.EmergencyContactPhone) ? null : req.EmergencyContactPhone.Trim(),
                    HeldSignatureDataUrl = waiver is not null ? req.SignatureDataUrl : null,
                    HeldWaiverId = waiver?.Id,
                    HeldSignedAt = waiver is not null ? DateTime.UtcNow : null,
                });
                if (photo is { } held)
                {
                    var heldKey = new RiderPhotoKey($"kiosk:{pendingId:D}", RiderPhotoRules.CredentialRiderKey, null, null);
                    var heldPhoto = await _photos.RecordForKey(tenantId, heldKey, held.Bytes, held.Extension, null);
                    await _kiosks.SetPhoto(pendingId, tenantId, heldPhoto.Id);
                }
                await _audit.Log("kiosk.signup", $"Kiosk sign-up on \"{device.Name}\" (matches an existing account: held for ID check)",
                    targetKind: "kiosk_signup", targetId: pendingId, tenantId: tenantId,
                    metadata: new { kioskDeviceId = device.Id, minor = plan.IsMinor, pending = true });
                return new ApiResponses().OkResult(new { CheckedIn = true });
            }

            // ── A brand-new profile: created now, with the signature and photo attached ──
            OtherRiderSaved saved;
            try
            {
                saved = (await _profiles.Save(new[] { plan }, null))[0];
            }
            catch (InvalidOperationException)
            {
                return new ApiResponses().BadRequestResult(StaffEmailRefused);
            }
            var attached = await _writer.Attach(tenantId, tenant.Timezone, plan, saved, waiver?.Id, req.SignatureDataUrl, ip,
                guardianName, req.GuardianPhone, req.GuardianEmail?.Trim(), photo, replacePhotoOnFile: true, staffUserId: null);
            if (attached.Error is not null) return new ApiResponses().BadRequestResult(attached.Error);

            var signupId = await _kiosks.CreateSignup(new KioskSignup
            {
                TenantId = tenantId,
                KioskDeviceId = device.Id,
                UserId = saved.RiderUserId,
                DependentId = saved.DependentId,
                GuardianUserId = saved.GuardianUserId,
                FirstName = plan.FirstName,
                LastName = plan.LastName,
                Birthdate = plan.Birthdate.ToDateTime(TimeOnly.MinValue),
                IsMinor = plan.IsMinor,
                SignatureId = attached.SignatureId,
                PhotoId = attached.PhotoId,
            });
            await _audit.Log("kiosk.signup", $"Kiosk sign-up on \"{device.Name}\"", targetKind: "kiosk_signup",
                targetId: signupId, tenantId: tenantId,
                metadata: new { kioskDeviceId = device.Id, minor = plan.IsMinor, signed = attached.SignatureId is not null, photo = attached.PhotoId is not null });
            return new ApiResponses().OkResult(new { CheckedIn = true });
        }

        private async Task<Services.Repositories.Data.PaymentData.TenantWaiver?> ActiveWaiver(Guid tenantId)
        {
            var w = await _waivers.GetActive(tenantId);
            return w is not null && (w.ExpiresAt is null || w.ExpiresAt > DateTime.UtcNow) ? w : null;
        }

        private static string PhotoMode(string? mode) =>
            IdVerificationWindow.IsValidMode(mode) ? mode! : IdVerificationWindow.ModeOff;
    }
}
