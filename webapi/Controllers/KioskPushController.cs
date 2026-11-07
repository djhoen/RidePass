using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Data.KioskData;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Kiosk;
using webapi.Helpers;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    /// <summary>
    /// Staff push a waiver and / or photo step for one rider to one front-desk kiosk (Script0311), from the gate
    /// (order waiver panel, pass card), Event Riders, the Gate Sale receipt or the kiosk sign-ups list. Either
    /// gate permission may (sales.redeem or sales.counter). One request per kiosk at a time; it expires after
    /// 10 minutes. The kiosk shows it only when idle, so a walk-up mid sign-up is never interrupted.
    /// </summary>
    [ApiController]
    [Route("api/KioskPushes")]
    [Authorize]
    public class KioskPushController : ControllerBase
    {
        /// <summary>A kiosk counts as available when seen this recently.</summary>
        private static readonly TimeSpan ActiveWindow = TimeSpan.FromMinutes(5);
        /// <summary>An idle kiosk asks for work every few seconds; longer than this = someone is using it.</summary>
        private static readonly TimeSpan IdleWindow = TimeSpan.FromSeconds(12);

        private readonly IKioskRepository _kiosks;
        private readonly IKioskPushService _pushes;
        private readonly IAuthorizationService _authz;
        private readonly Services.Audit.IAuditLogger _audit;
        private readonly ITenantContext _tenantContext;

        public KioskPushController(IKioskRepository kiosks, IKioskPushService pushes, IAuthorizationService authz,
            Services.Audit.IAuditLogger audit, ITenantContext tenantContext)
        {
            _kiosks = kiosks;
            _pushes = pushes;
            _authz = authz;
            _audit = audit;
            _tenantContext = tenantContext;
        }

        private async Task<bool> MayUse() =>
            (await _authz.AuthorizeAsync(User, TenantPermissions.Policy.SalesRedeem)).Succeeded
            || (await _authz.AuthorizeAsync(User, TenantPermissions.Policy.SalesCounter)).Succeeded;

        private Guid? StaffId() => Guid.TryParse(User.FindFirst("UserId")?.Value, out var u) ? u : null;

        private static bool IsActive(KioskDevice d) =>
            d.CredentialHash is not null && d.RevokedAt is null
            && d.LastSeenAt is { } seen && DateTime.SpecifyKind(seen, DateTimeKind.Utc) > DateTime.UtcNow - ActiveWindow;

        private static bool IsIdle(KioskDevice d) =>
            d.LastPollAt is { } p && DateTime.SpecifyKind(p, DateTimeKind.Utc) > DateTime.UtcNow - IdleWindow;

        /// <summary>The kiosks a step can be sent to now.</summary>
        [HttpGet("Kiosks")]
        public async Task<IActionResult> Kiosks()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!await MayUse()) return Forbid();
            var tenantId = _tenantContext.TenantId;
            var items = new List<ActiveKioskItem>();
            // Every open request in one read (at most one per kiosk).
            var open = (await _kiosks.ListOpenPushes(tenantId)).ToDictionary(p => p.KioskDeviceId);
            foreach (var d in (await _kiosks.ListDevices(tenantId)).Where(IsActive))
            {
                var active = open.GetValueOrDefault(d.Id);
                items.Add(new ActiveKioskItem
                {
                    Id = d.Id, Name = d.Name,
                    LastSeenAtUtc = d.LastSeenAt is { } s ? DateTime.SpecifyKind(s, DateTimeKind.Utc) : null,
                    Idle = IsIdle(d), Busy = active is not null, ActivePushId = active?.Id,
                });
            }
            return new ApiResponses().OkResult(items);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateKioskPushRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!await MayUse()) return Forbid();
            var tenantId = _tenantContext.TenantId;
            var device = await _kiosks.GetDevice(req.KioskDeviceId, tenantId);
            if (device is null || !IsActive(device))
                return new ApiResponses().BadRequestResult("That kiosk isn't available (not paired, removed, or not seen in the last few minutes).");
            var (push, error) = await _pushes.Build(tenantId, req, StaffId());
            if (error is not null) return new ApiResponses().BadRequestResult(error);
            var created = await _kiosks.CreatePush(push!);
            if (created is null)
            {
                var current = await _kiosks.GetActivePush(tenantId, device.Id);
                return new ApiResponses().ConflictResult(
                    $"{device.Name} already has a request waiting or on screen. Cancel it first to send this one.",
                    new { code = "kiosk_busy", activePushId = current?.Id });
            }
            await _audit.Log("kiosk.push_sent",
                $"Sent {(created.WantWaiver && created.WantPhoto ? "waiver and photo" : created.WantWaiver ? "waiver" : "photo")} for {created.RiderFirstName} to kiosk \"{device.Name}\"",
                targetKind: "kiosk_push", targetId: created.Id, tenantId: tenantId,
                metadata: new { created.TargetKind, created.EventTicketPurchaseId, created.SeasonPassPurchaseId, created.KioskSignupId, created.SignMode });
            return new ApiResponses().OkResult(ToStatus(created, device));
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> Get(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!await MayUse()) return Forbid();
            var push = await _kiosks.GetPush(id, _tenantContext.TenantId);
            if (push is null) return new ApiResponses().NotFoundResult("Request not found.");
            var device = await _kiosks.GetDevice(push.KioskDeviceId, _tenantContext.TenantId);
            return new ApiResponses().OkResult(ToStatus(push, device));
        }

        [HttpPost("{id:guid}/Cancel")]
        public async Task<IActionResult> Cancel(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!await MayUse()) return Forbid();
            var push = await _kiosks.GetPush(id, _tenantContext.TenantId);
            if (push is null) return new ApiResponses().NotFoundResult("Request not found.");
            if (!await _kiosks.CancelPush(id, _tenantContext.TenantId, StaffId()))
                return new ApiResponses().BadRequestResult(push.Status == KioskPush.StatusCompleting
                    ? "The kiosk is saving it right now."
                    : $"This request is already {push.Status.Replace('_', ' ')}.");
            return new ApiResponses().OkResult();
        }

        private static KioskPushStatusResponse ToStatus(KioskPush p, KioskDevice? d) => new()
        {
            Id = p.Id,
            Status = p.Status,
            KioskName = d?.Name ?? "Kiosk",
            KioskIdle = d is not null && IsIdle(d),
            RiderFirstName = p.RiderFirstName,
            WantWaiver = p.WantWaiver,
            WantPhoto = p.WantPhoto,
            ExpiresAtUtc = DateTime.SpecifyKind(p.ExpiresAt, DateTimeKind.Utc),
            CompletedAtUtc = p.CompletedAt is { } c ? DateTime.SpecifyKind(c, DateTimeKind.Utc) : null,
            EndReason = p.EndReason,
        };
    }
}
