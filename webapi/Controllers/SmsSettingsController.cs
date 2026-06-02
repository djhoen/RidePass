using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Services.Audit;
using Services.Helpers;
using Services.Helpers.Interfaces;
using Services.Repositories.Interfaces;
using Services.Sms;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.SmsSettings;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SmsSettingsController : ControllerBase
    {
        private readonly ITenantContext _tenantContext;
        private readonly ITenantRepository _tenants;
        private readonly ITwilioSubaccountProvisioner _provisioner;
        private readonly ISmsPricing _pricing;
        private readonly IAuditLogger _audit;

        public SmsSettingsController(
            ITenantContext tenantContext,
            ITenantRepository tenants,
            ITwilioSubaccountProvisioner provisioner,
            ISmsPricing pricing,
            IAuditLogger audit)
        {
            _tenantContext = tenantContext;
            _tenants = tenants;
            _provisioner = provisioner;
            _pricing = pricing;
            _audit = audit;
        }

        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpGet("Status")]
        public IActionResult Status()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var t = _tenantContext.Tenant;
            return new ApiResponses().OkResult(new SmsStatusResponse
            {
                Enabled = t.SmsEnabled,
                HasProvisionedNumber = !string.IsNullOrWhiteSpace(t.TwilioFromNumber),
                PhoneNumber = t.TwilioFromNumber,
                EnabledAtUtc = t.SmsEnabledAtUtc,
                MasterConfigured = _provisioner.IsMasterConfigured,
                OutboundPerSegmentCents = _pricing.OutboundPerSegmentCents,
            });
        }

        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [EnableRateLimiting("sms-search")]
        [HttpGet("Search")]
        public async Task<IActionResult> Search(
            [FromQuery] string? areaCode, [FromQuery] int max = 10, CancellationToken ct = default)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");

            try
            {
                var nums = await _provisioner.SearchTollFreeNumbers(areaCode, max, ct);
                var dtos = nums.Select(n => new AvailableNumberDto
                {
                    PhoneNumber = n.PhoneNumber,
                    FriendlyName = n.FriendlyName,
                    Region = n.Region,
                    IsoCountry = n.IsoCountry,
                }).ToList();
                return new ApiResponses().OkResult(dtos);
            }
            catch (TwilioProvisioningException ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [EnableRateLimiting("sms-provision")]
        [HttpPost("Provision")]
        public async Task<IActionResult> Provision([FromBody] ProvisionSmsRequest req, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (req is null || string.IsNullOrWhiteSpace(req.PhoneNumber))
                return new ApiResponses().BadRequestResult("phoneNumber is required.");

            try
            {
                var result = await _provisioner.ProvisionTenant(_tenantContext.Tenant, req.PhoneNumber, ct);
                await _audit.Log("sms.provision",
                    $"Provisioned toll-free number {result.PhoneNumber}",
                    targetKind: "tenant", targetId: _tenantContext.TenantId,
                    metadata: new { phoneNumber = result.PhoneNumber, subaccountSid = result.SubaccountSid });
                return new ApiResponses().OkResult(new ProvisionSmsResponse { PhoneNumber = result.PhoneNumber });
            }
            catch (TwilioProvisioningException ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpPost("Enable")]
        public async Task<IActionResult> Enable()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (string.IsNullOrWhiteSpace(_tenantContext.Tenant.TwilioFromNumber))
                return new ApiResponses().BadRequestResult("SMS isn't provisioned yet — provision a number first.");

            await _tenants.SetSmsEnabled(_tenantContext.TenantId, true);
            await _audit.Log("sms.enable", "Enabled SMS sending",
                targetKind: "tenant", targetId: _tenantContext.TenantId);
            return new ApiResponses().OkResult(new { enabled = true });
        }

        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpPost("Disable")]
        public async Task<IActionResult> Disable()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            await _tenants.SetSmsEnabled(_tenantContext.TenantId, false);
            await _audit.Log("sms.disable", "Paused SMS sending",
                targetKind: "tenant", targetId: _tenantContext.TenantId);
            return new ApiResponses().OkResult(new { enabled = false });
        }

        /// <summary>
        /// Permanently release the tenant's Twilio provisioning — closes the
        /// subaccount, which Twilio cascades into releasing the number and
        /// deleting the Messaging Service. Destructive; the UI requires
        /// confirmation. After release the tenant can provision a fresh
        /// number (a different one — Twilio doesn't guarantee re-availability
        /// of the released number).
        /// </summary>
        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [EnableRateLimiting("sms-provision")]
        [HttpPost("Release")]
        public async Task<IActionResult> Release(CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var tenant = _tenantContext.Tenant;
            if (string.IsNullOrWhiteSpace(tenant.TwilioSubaccountSid))
            {
                return new ApiResponses().BadRequestResult("No SMS provisioning to release.");
            }

            var releasedNumber = tenant.TwilioFromNumber;
            var releasedSid = tenant.TwilioSubaccountSid;

            try
            {
                await _provisioner.ReleaseTenant(tenant, ct);
            }
            catch (TwilioProvisioningException ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }

            await _audit.Log("sms.release",
                $"Released toll-free number {releasedNumber}",
                targetKind: "tenant", targetId: _tenantContext.TenantId,
                metadata: new { phoneNumber = releasedNumber, subaccountSid = releasedSid });

            return new ApiResponses().OkResult(new { released = true });
        }
    }
}
