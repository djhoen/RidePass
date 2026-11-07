using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Interfaces;
using Services.Waivers;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Kiosk;
using webapi.Helpers;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    /// <summary>
    /// Kiosk sign-ups on the gate screens (Script0310): today's, newest first, for the Gate Sale (to pick one
    /// as the customer) and Scan Tickets. Either gate permission may read them (sales.counter or sales.redeem).
    /// </summary>
    [ApiController]
    [Route("api/KioskSignups")]
    [Authorize]
    public class KioskSignupsController : ControllerBase
    {
        private readonly IKioskRepository _kiosks;
        private readonly IAuthorizationService _authz;
        private readonly ITenantContext _tenantContext;

        public KioskSignupsController(IKioskRepository kiosks, IAuthorizationService authz, ITenantContext tenantContext)
        {
            _kiosks = kiosks;
            _authz = authz;
            _tenantContext = tenantContext;
        }

        private async Task<bool> MayUse() =>
            (await _authz.AuthorizeAsync(User, TenantPermissions.Policy.SalesCounter)).Succeeded
            || (await _authz.AuthorizeAsync(User, TenantPermissions.Policy.SalesRedeem)).Succeeded;

        [HttpGet]
        public async Task<IActionResult> Today()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!await MayUse()) return Forbid();
            var tz = _tenantContext.Tenant.Timezone;
            var today = TenantClock.LocalDate(tz, DateTime.UtcNow);
            // Today in the venue's own time zone.
            var zone = TenantClock.Resolve(tz);
            var sinceUtc = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(today.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified), zone);
            var rows = await _kiosks.ListSignups(_tenantContext.TenantId, sinceUtc, 100);
            return new ApiResponses().OkResult(rows.Select(r =>
            {
                var bd = GuardianWaiverRule.ToDateOnly(r.Birthdate);
                return new KioskSignupItem
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
                    GuardianName = r.GuardianUserId is null ? null : $"{r.GuardianFirstName} {r.GuardianLastName}".Trim(),
                    GuardianEmail = r.GuardianEmail,
                    WaiverSigned = r.SignatureId is not null,
                    PhotoId = r.PhotoId,
                    PhotoTakenAtUtc = r.PhotoTakenAt is { } p ? DateTime.SpecifyKind(p, DateTimeKind.Utc) : null,
                    KioskName = r.KioskName,
                    CreatedAtUtc = DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc),
                    HandledAtUtc = r.HandledAt is { } h ? DateTime.SpecifyKind(h, DateTimeKind.Utc) : null,
                    DismissedAtUtc = r.DismissedAt is { } x ? DateTime.SpecifyKind(x, DateTimeKind.Utc) : null,
                };
            }).ToList());
        }

        [HttpPost("{id:guid}/Dismiss")]
        public async Task<IActionResult> Dismiss(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!await MayUse()) return Forbid();
            var userId = Guid.TryParse(User.FindFirst("UserId")?.Value, out var u) ? u : (Guid?)null;
            if (await _kiosks.GetSignup(id, _tenantContext.TenantId) is null)
                return new ApiResponses().NotFoundResult("Sign-up not found.");
            await _kiosks.Dismiss(id, _tenantContext.TenantId, userId);
            return new ApiResponses().OkResult();
        }
    }
}
