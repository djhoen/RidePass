using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Wristband;
using webapi.Helpers;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    /// <summary>
    /// Daily wristbands (Script0312) for a rider admitted today without one, or a replacement for a lost or
    /// broken band. Admission itself bands in the same step (SeasonPass/Pass/{token}/Redeem with a band code);
    /// this is for the band afterwards. Every rule lives in IRiderDayBands.
    /// </summary>
    [ApiController]
    [Route("api/RiderDayBands")]
    [Authorize(Policy = TenantPermissions.Policy.SalesRedeem)]
    public class RiderDayBandController : ControllerBase
    {
        private readonly ISeasonPassRepository _passes;
        private readonly IRiderDayBands _dayBands;
        private readonly Services.Helpers.Interfaces.IDbHelper _db;
        private readonly ITenantContext _tenantContext;

        public RiderDayBandController(ISeasonPassRepository passes, IRiderDayBands dayBands,
            Services.Helpers.Interfaces.IDbHelper db, ITenantContext tenantContext)
        {
            _passes = passes;
            _dayBands = dayBands;
            _db = db;
            _tenantContext = tenantContext;
        }

        private Guid? StaffId() => Guid.TryParse(User.FindFirst("UserId")?.Value, out var u) ? u : null;

        /// <summary>Bands the holder of a pass admitted today, or replaces their band (with a reason).</summary>
        [HttpPost("Pass/{token:guid}")]
        public async Task<IActionResult> BandPassHolder(Guid token, [FromBody] BandPassHolderRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!_tenantContext.Tenant.WristbandsEnabled)
                return new ApiResponses().BadRequestResult("Wristbands aren't turned on for this track (Settings → Features).");
            var tenantId = _tenantContext.TenantId;
            var pass = await _passes.GetPurchaseByRedemptionToken(token);
            if (pass is null || pass.TenantId != tenantId) return new ApiResponses().NotFoundResult("Pass not found.");
            var reason = string.IsNullOrWhiteSpace(req.ReplaceReason) ? null : req.ReplaceReason.Trim();
            if (reason is not null && reason.Length > 200) return new ApiResponses().BadRequestResult("Keep the reason short.");

            // Serialized with admission of the same pass (the same lock RedeemPassAtGate takes).
            await using var redeemLock = await _db.AcquireAdvisoryLock($"season-pass-redeem:{pass.Id}");
            var (atUtc, untilUtc) = Services.Helpers.TenantClock.DayRangeUtc(_tenantContext.Tenant.Timezone, _dayBands.Today());
            var today = _dayBands.Today().ToDateTime(TimeOnly.MinValue);
            var admitted = (await _passes.ListReservationsForPurchaseOnDate(pass.Id, tenantId, atUtc, untilUtc, today))
                .Where(r => r.Status == "checked_in")
                .OrderByDescending(r => r.CheckedInAt ?? DateTime.MinValue)
                .ToList();
            if (admitted.Count == 0)
                return new ApiResponses().BadRequestResult("This pass hasn't been admitted today. Admit it first (Admit and band).");

            var latest = admitted[0];
            var product = await _passes.GetProduct(pass.ProductId, tenantId);
            var result = await _dayBands.IssueForPass(pass, product, latest.Id, latest.EventId, req.Code, reason, StaffId());
            if (!result.Ok)
                return result.GateBlock is { } block
                    ? GateBlockResult.Result(block, "season_pass", pass.RedemptionToken, pass.Id, latest.EventId)
                    : new ApiResponses().BadRequestResult(result.Error!, new { code = "band_refused" });
            // Every admission the holder has today is covered by the one band.
            foreach (var r in admitted.Skip(1)) await _dayBands.LinkAdmission(result.Band!.Id, r.Id, StaffId());
            var band = result.Band!;
            return new ApiResponses().OkResult(new
            {
                band.Id,
                band.Code,
                IssuedAtUtc = DateTime.SpecifyKind(band.IssuedAt, DateTimeKind.Utc),
                IssuedAtLocal = _dayBands.IssuedTime(band),
                Replaced = reason is not null,
            });
        }
    }
}
