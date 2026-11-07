using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Data.KioskData;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Kiosk;
using webapi.Helpers;
using webapi.Multitenancy;
using webapi.Security;

namespace webapi.Controllers
{
    /// <summary>
    /// Admin > Kiosks (Script0310): a manager (settings.manage) adds a front-desk tablet, gets a one-time
    /// pairing code to type on it, renames, re-pairs or revokes it. The tablet never receives a staff
    /// session: it swaps the code for its own secret (KioskController.Pair).
    /// </summary>
    [ApiController]
    [Route("api/KioskDevices")]
    [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
    public class KioskDevicesController : ControllerBase
    {
        private readonly IKioskRepository _kiosks;
        private readonly Services.Audit.IAuditLogger _audit;
        private readonly KioskCredentialCache _known;
        private readonly ITenantContext _tenantContext;

        public KioskDevicesController(IKioskRepository kiosks, Services.Audit.IAuditLogger audit, ITenantContext tenantContext,
            KioskCredentialCache known)
        {
            _kiosks = kiosks;
            _audit = audit;
            _known = known;
            _tenantContext = tenantContext;
        }

        [HttpGet]
        public async Task<IActionResult> List()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var rows = await _kiosks.ListDevices(_tenantContext.TenantId);
            return new ApiResponses().OkResult(rows.Select(ToItem).ToList());
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] KioskDeviceNameRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var name = CleanName(req.Name);
            if (name is null) return new ApiResponses().BadRequestResult("Name the kiosk (for example \"Gate kiosk 1\").");
            var code = KioskCredentials.NewPairingCode();
            var expires = DateTime.UtcNow.Add(KioskCredentials.PairingLifetime);
            var device = await _kiosks.CreateDevice(_tenantContext.TenantId, name,
                KioskCredentials.HashPairingCode(_tenantContext.TenantId, code), expires, StaffId());
            await _audit.Log("kiosk.created", $"Kiosk \"{name}\" added", targetKind: "kiosk_device",
                targetId: device.Id, tenantId: _tenantContext.TenantId);
            return new ApiResponses().OkResult(new KioskPairingCodeResponse
            {
                DeviceId = device.Id, Name = device.Name, Code = KioskCredentials.Display(code), ExpiresAtUtc = expires,
            });
        }

        /// <summary>A new pairing code (to pair a replacement tablet): once used, the old tablet's secret stops working.</summary>
        [HttpPost("{id:guid}/PairingCode")]
        public async Task<IActionResult> NewPairingCode(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var device = await _kiosks.GetDevice(id, _tenantContext.TenantId);
            if (device is null) return new ApiResponses().NotFoundResult("Kiosk not found.");
            if (device.RevokedAt is not null) return new ApiResponses().BadRequestResult("This kiosk was removed. Add a new one.");
            var code = KioskCredentials.NewPairingCode();
            var expires = DateTime.UtcNow.Add(KioskCredentials.PairingLifetime);
            await _kiosks.SetPairingCode(id, _tenantContext.TenantId, KioskCredentials.HashPairingCode(_tenantContext.TenantId, code), expires);
            return new ApiResponses().OkResult(new KioskPairingCodeResponse
            {
                DeviceId = device.Id, Name = device.Name, Code = KioskCredentials.Display(code), ExpiresAtUtc = expires,
            });
        }

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Rename(Guid id, [FromBody] KioskDeviceNameRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var name = CleanName(req.Name);
            if (name is null) return new ApiResponses().BadRequestResult("Name the kiosk.");
            return await _kiosks.RenameDevice(id, _tenantContext.TenantId, name)
                ? new ApiResponses().OkResult()
                : new ApiResponses().NotFoundResult("Kiosk not found.");
        }

        [HttpPost("{id:guid}/Revoke")]
        public async Task<IActionResult> Revoke(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var device = await _kiosks.GetDevice(id, _tenantContext.TenantId);
            if (device is null) return new ApiResponses().NotFoundResult("Kiosk not found.");
            // Its key loses its own rate-limit partition at once (and fails authentication from now on).
            _known.Forget(device.CredentialHash);
            if (await _kiosks.RevokeDevice(id, _tenantContext.TenantId, StaffId()))
                await _audit.Log("kiosk.revoked", $"Kiosk \"{device.Name}\" removed", targetKind: "kiosk_device",
                    targetId: id, tenantId: _tenantContext.TenantId);
            return new ApiResponses().OkResult();
        }

        private static KioskDeviceItem ToItem(KioskDevice d) => new()
        {
            Id = d.Id,
            Name = d.Name,
            Paired = d.CredentialHash is not null,
            PairedAtUtc = Utc(d.PairedAt),
            LastSeenAtUtc = Utc(d.LastSeenAt),
            PairingPending = d.PairingCodeHash is not null && d.PairingExpiresAt > DateTime.UtcNow,
            PairingExpiresAtUtc = Utc(d.PairingExpiresAt),
            RevokedAtUtc = Utc(d.RevokedAt),
            CreatedAtUtc = DateTime.SpecifyKind(d.CreatedAt, DateTimeKind.Utc),
        };

        private static DateTime? Utc(DateTime? v) => v is { } x ? DateTime.SpecifyKind(x, DateTimeKind.Utc) : null;

        private static string? CleanName(string? name)
        {
            var n = name?.Trim();
            return string.IsNullOrEmpty(n) ? null : n.Length > 80 ? n[..80] : n;
        }

        private Guid? StaffId() => Guid.TryParse(User.FindFirst("UserId")?.Value, out var id) ? id : null;
    }
}
