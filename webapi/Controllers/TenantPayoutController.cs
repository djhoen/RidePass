using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Helpers;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    /// <summary>
    /// Tenant-facing read-only view of balance, ledger, and payout history. Mirrors the super
    /// admin endpoints but scoped to the resolved tenant.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = TenantPermissions.Policy.ReportsView)]
    public class TenantPayoutController : ControllerBase
    {
        private readonly ITenantLedgerRepository _ledger;
        private readonly ITenantPayoutRepository _payouts;
        private readonly ITenantContext _tenantContext;

        public TenantPayoutController(
            ITenantLedgerRepository ledger,
            ITenantPayoutRepository payouts,
            ITenantContext tenantContext)
        {
            _ledger = ledger;
            _payouts = payouts;
            _tenantContext = tenantContext;
        }

        [HttpGet("Payouts/{payoutId:guid}/Csv")]
        public async Task<IActionResult> GetPayoutCsv(Guid payoutId)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var payout = await _payouts.GetById(payoutId, _tenantContext.TenantId);
            if (payout is null) return new ApiResponses().NotFoundResult("Payout not found.");
            var entries = await _payouts.ListEntriesForPayout(payoutId);
            var tenant = _tenantContext.Tenant;
            var csv = PayoutCsvBuilder.Build(payout, entries, tenant.Subdomain, tenant.DisplayName);
            var bytes = System.Text.Encoding.UTF8.GetBytes(csv);
            return File(bytes, "text/csv", PayoutCsvBuilder.FilenameFor(payout, tenant.Subdomain));
        }

        [HttpGet("Balance")]
        public async Task<IActionResult> GetBalance()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var summary = await _ledger.GetSummary(_tenantContext.TenantId);
            return new ApiResponses().OkResult(summary);
        }

        [HttpGet("Ledger")]
        public async Task<IActionResult> ListLedger([FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc, [FromQuery] int take = 200)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var entries = await _ledger.ListByTenant(_tenantContext.TenantId, fromUtc, toUtc, Math.Clamp(take, 1, 1000));
            return new ApiResponses().OkResult(entries);
        }

        [HttpGet("Payouts")]
        public async Task<IActionResult> ListPayouts()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var payouts = await _payouts.ListByTenant(_tenantContext.TenantId);
            return new ApiResponses().OkResult(payouts);
        }

        [HttpGet("Payouts/{payoutId:guid}")]
        public async Task<IActionResult> GetPayout(Guid payoutId)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var payout = await _payouts.GetById(payoutId, _tenantContext.TenantId);
            if (payout is null) return new ApiResponses().NotFoundResult("Payout not found.");
            var entries = await _payouts.ListEntriesForPayout(payoutId);
            return new ApiResponses().OkResult(new { payout, entries });
        }
    }
}
