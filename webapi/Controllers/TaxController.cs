using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Data.TaxData;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Tax;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    /// <summary>
    /// Per-tenant tax settings. Today this exposes the event admission/amusement tax rate, kept
    /// separate from the concession sales tax because amusement tax is usually a different local
    /// rate the tenant confirms with their municipality. The tenant is the merchant of record for
    /// the admissions it sells and remits this tax; RidePass calculates and collects it at checkout.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class TaxController : ControllerBase
    {
        private const string AdmissionKind = "admission";

        private readonly ITenantTaxRepository _tax;
        private readonly ITenantContext _tenantContext;

        public TaxController(ITenantTaxRepository tax, ITenantContext tenantContext)
        {
            _tax = tax;
            _tenantContext = tenantContext;
        }

        // Public (anonymous) read: both the admin settings screen and the guest ticket checkout need
        // the rate to show a tax line, and the rate isn't sensitive (the buyer sees it at checkout
        // regardless). Still requires a resolved tenant. Missing row = no tax (0%, on top, fee taxable).
        [AllowAnonymous]
        [HttpGet("Admission")]
        public async Task<IActionResult> GetAdmission()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var row = await _tax.GetByKind(_tenantContext.TenantId, AdmissionKind);
            return new ApiResponses().OkResult(Map(row));
        }

        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpPut("Admission")]
        public async Task<IActionResult> UpdateAdmission([FromBody] UpdateAdmissionTaxRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var saved = await _tax.Upsert(new TenantTaxRate
            {
                TenantId = _tenantContext.TenantId,
                TaxKind = AdmissionKind,
                RateBps = Math.Clamp(req.RateBps, 0, 10000),
                PricesIncludeTax = req.PricesIncludeTax,
                ServiceChargeTaxable = req.ServiceChargeTaxable,
                JurisdictionLabel = string.IsNullOrWhiteSpace(req.JurisdictionLabel) ? null : req.JurisdictionLabel.Trim(),
                IsActive = req.IsActive,
            });
            return new ApiResponses().OkResult(Map(saved));
        }

        private static AdmissionTaxResponse Map(TenantTaxRate? row) => new()
        {
            RateBps = row?.RateBps ?? 0,
            PricesIncludeTax = row?.PricesIncludeTax ?? false,
            ServiceChargeTaxable = row?.ServiceChargeTaxable ?? true,
            JurisdictionLabel = row?.JurisdictionLabel,
            IsActive = row?.IsActive ?? true,
        };
    }
}
