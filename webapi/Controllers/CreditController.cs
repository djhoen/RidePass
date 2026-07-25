using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Credit;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    // Store credit: per-tenant customer balances. Viewing is CustomersView; moving money
    // (grants/corrections) is SalesRefund; the counter lookup accepts any counter so the shop
    // register can take credit as a tender. Credit issuance/redemption never writes
    // tenant_ledger_entry rows (see Script0193's header for the accounting rules).
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class CreditController : ControllerBase
    {
        private readonly ITenantCreditRepository _credit;
        private readonly ITenantContext _tenantContext;
        private readonly Services.Audit.IAuditLogger _audit;

        public CreditController(ITenantCreditRepository credit, ITenantContext tenantContext,
            Services.Audit.IAuditLogger audit)
        {
            _credit = credit;
            _tenantContext = tenantContext;
            _audit = audit;
        }

        private Guid TenantId => _tenantContext.TenantId;
        private Guid? UserId => Guid.TryParse(User.FindFirst("UserId")?.Value, out var id) ? id : null;

        [Authorize(Policy = TenantPermissions.Policy.CustomersView)]
        [HttpGet("Accounts")]
        public async Task<IActionResult> Search([FromQuery] string? query, [FromQuery] int limit = 50)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var accounts = await _credit.SearchAccounts(TenantId, query, Math.Clamp(limit, 1, 200));
            var outstanding = await _credit.OutstandingTotal(TenantId);
            return new ApiResponses().OkResult(new { accounts, outstandingCents = outstanding });
        }

        [Authorize(Policy = TenantPermissions.Policy.CustomersView)]
        [HttpGet("Accounts/{id:guid}/Entries")]
        public async Task<IActionResult> Entries(Guid id, [FromQuery] int limit = 100)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var account = await _credit.GetAccount(id, TenantId);
            if (account is null) return new ApiResponses().NotFoundResult("Credit account not found.");
            return new ApiResponses().OkResult(new
            {
                account,
                entries = await _credit.ListEntries(id, TenantId, Math.Clamp(limit, 1, 500)),
            });
        }

        [Authorize(Policy = TenantPermissions.Policy.SalesRefund)]
        [HttpPost("Accounts")]
        public async Task<IActionResult> Create([FromBody] CreateCreditAccountRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var account = await _credit.GetOrCreateAccount(TenantId, req.UserId, req.Email, req.Phone, req.DisplayName);
            if (account is null)
                return new ApiResponses().BadRequestResult("Give the account an email or phone so it can be looked up later.");
            return new ApiResponses().OkResult(account);
        }

        // Manual grant or correction. Grants are the tenant giving away value (goodwill,
        // promo); corrections claw back mistakes. Redemptions happen at the registers, not here.
        [Authorize(Policy = TenantPermissions.Policy.SalesRefund)]
        [HttpPost("Accounts/{id:guid}/Adjust")]
        public async Task<IActionResult> Adjust(Guid id, [FromBody] AdjustCreditRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (req.DeltaCents == 0) return new ApiResponses().BadRequestResult("Enter a non-zero amount.");
            var account = await _credit.GetAccount(id, TenantId);
            if (account is null) return new ApiResponses().NotFoundResult("Credit account not found.");
            if (!await _credit.TryAdjust(id, TenantId, req.DeltaCents, "manual_adjust", null, null,
                    string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim(), UserId))
                return new ApiResponses().BadRequestResult(
                    $"The account only has {Money(account.BalanceCents)} available.");
            var updated = await _credit.GetAccount(id, TenantId);

            // A manual adjustment mints spendable value with nothing behind it: no sale, no
            // payment, no processor record. Anyone holding sales.refund can grant an arbitrary
            // amount to any account, so this is the single most direct way to convert access into
            // money and belongs in the audit trail with the before/after balance.
            await _audit.Log(
                "credit.manual_adjust",
                $"Adjusted store credit by {Money(req.DeltaCents)} for {account.Email ?? account.DisplayName ?? "an account"} "
                    + $"({Money(account.BalanceCents)} to {Money(updated?.BalanceCents ?? account.BalanceCents + req.DeltaCents)})",
                targetKind: "credit_account",
                targetId: id,
                tenantId: TenantId,
                metadata: new
                {
                    deltaCents = req.DeltaCents,
                    balanceBeforeCents = account.BalanceCents,
                    balanceAfterCents = updated?.BalanceCents,
                    accountEmail = account.Email,
                    note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim(),
                });

            return new ApiResponses().OkResult(updated);
        }

        // Counter lookup: exact email or phone. Any counter permission qualifies (shop register,
        // F&B POS, gate); [Authorize(Policy=...)] can only AND policies, so the OR of the two
        // counter permissions is checked by hand here, mirroring TenantPermissionHandler's claim
        // reading. The projection is only what a register needs (name + balance).
        [HttpGet("Lookup")]
        public async Task<IActionResult> Lookup([FromQuery] string query)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var roles = User.FindAll("role").Select(c => c.Value).ToList();
            var perms = TenantPermissions.ForRoles(roles);
            // Any counter that can take store credit as a tender may look an account up: the gate,
            // the F&B window, and the bike shop. [Authorize] policies can only AND, so this is a
            // manual OR.
            if (!roles.Contains("super_admin")
                && !perms.Contains(TenantPermissions.SalesCounter)
                && !perms.Contains(TenantPermissions.ConcessionsCounter)
                && !perms.Contains(TenantPermissions.ShopCounter))
                return Forbid();
            if (string.IsNullOrWhiteSpace(query))
                return new ApiResponses().BadRequestResult("Enter the customer's email or phone.");
            var account = await _credit.LookupAccount(TenantId, query.Trim());
            if (account is null)
                return new ApiResponses().NotFoundResult("No store credit account matches that email or phone.");
            return new ApiResponses().OkResult(new
            {
                id = account.Id,
                displayName = account.DisplayName,
                balanceCents = account.BalanceCents,
            });
        }

        // The signed-in rider's own balance + recent history (the "my credit" view and the
        // online-checkout offer). Any authenticated user: it resolves strictly by their own
        // user id within the resolved tenant, so there is nothing to leak.
        [HttpGet("Mine")]
        public async Task<IActionResult> Mine()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (UserId is null) return new ApiResponses().BadRequestResult("Not signed in.");
            var account = await _credit.GetAccountForUser(TenantId, UserId.Value);
            if (account is null)
                return new ApiResponses().OkResult(new { balanceCents = 0, entries = Array.Empty<object>() });
            var entries = await _credit.ListEntries(account.Id, TenantId, 50);
            return new ApiResponses().OkResult(new
            {
                balanceCents = account.BalanceCents,
                entries = entries.Select(e => new { e.DeltaCents, e.Kind, e.Note, e.CreatedAt }),
            });
        }

        private static string Money(int cents) => "$" + (cents / 100m).ToString("0.00");
    }
}
