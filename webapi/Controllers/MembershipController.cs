using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Payments;
using Services.Repositories.Data.MembershipData;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Membership;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MembershipController : ControllerBase
    {
        private readonly IMembershipRepository _memberships;
        private readonly IUserRepository _users;
        private readonly ITenantRepository _tenants;
        private readonly IPaymentProvider _payments;
        private readonly IChargeRouter _chargeRouter;
        private readonly ITenantContext _tenantContext;

        public MembershipController(
            IMembershipRepository memberships,
            IUserRepository users,
            ITenantRepository tenants,
            IPaymentProvider payments,
            IChargeRouter chargeRouter,
            ITenantContext tenantContext)
        {
            _memberships = memberships;
            _users = users;
            _tenants = tenants;
            _payments = payments;
            _chargeRouter = chargeRouter;
            _tenantContext = tenantContext;
        }

        // Combined "what's offered here + my status". Anonymous-friendly so the
        // rider /Membership page can render the price card before sign-in.
        [HttpGet("Status")]
        public async Task<IActionResult> Status()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var t = _tenantContext.Tenant;
            var resp = new MembershipStatusResponse
            {
                Enabled = t.MembershipEnabled,
                Name = t.MembershipName,
                PriceCents = t.MembershipPriceCents,
                DurationKind = t.MembershipDurationKind,
                RequiredForRiders = t.MembershipRequiredForRiders,
                RequiredForSpectators = t.MembershipRequiredForSpectators,
            };

            if (User.Identity?.IsAuthenticated == true && Guid.TryParse(User.FindFirst("UserId")?.Value, out var userId))
            {
                var active = await _memberships.GetActive(userId, t.Id, DateTime.UtcNow);
                if (active is not null)
                {
                    resp.Active = new ActiveMembership
                    {
                        Id = active.Id,
                        Name = active.NameAtPurchase,
                        DurationKind = active.DurationKind,
                        ValidFromUtc = DateTime.SpecifyKind(active.ValidFromUtc, DateTimeKind.Utc),
                        ValidToUtc = active.ValidToUtc.HasValue
                            ? DateTime.SpecifyKind(active.ValidToUtc.Value, DateTimeKind.Utc)
                            : null,
                        AmountCents = active.AmountCents,
                    };
                }
                var history = await _memberships.ListMine(userId, t.Id);
                resp.History = history.Select(h => new MembershipHistoryItem
                {
                    Id = h.Id,
                    Name = h.NameAtPurchase,
                    DurationKind = h.DurationKind,
                    ValidFromUtc = DateTime.SpecifyKind(h.ValidFromUtc, DateTimeKind.Utc),
                    ValidToUtc = h.ValidToUtc.HasValue ? DateTime.SpecifyKind(h.ValidToUtc.Value, DateTimeKind.Utc) : null,
                    AmountCents = h.AmountCents,
                    Status = h.Status,
                    CreatedAtUtc = DateTime.SpecifyKind(h.CreatedAt, DateTimeKind.Utc),
                }).ToList();
            }

            return new ApiResponses().OkResult(resp);
        }

        [Authorize]
        [HttpPost("Buy")]
        public async Task<IActionResult> Buy(CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!Guid.TryParse(User.FindFirst("UserId")?.Value, out var userId))
                return new ApiResponses().BadRequestResult("Invalid token.");

            var tenant = _tenantContext.Tenant;
            if (!tenant.MembershipEnabled || tenant.MembershipPriceCents <= 0)
            {
                return new ApiResponses().BadRequestResult("Memberships aren't sold at this track.");
            }

            var user = await _users.GetById(userId);
            if (user is null) return new ApiResponses().BadRequestResult("User not found.");

            // Frozen pricing & duration on the row so historical reads are stable.
            var now = DateTime.UtcNow;
            DateTime? validTo = tenant.MembershipDurationKind == "yearly" ? now.AddDays(365) : (DateTime?)null;

            var serviceCharge = (int)((long)tenant.MembershipPriceCents * tenant.ServiceChargeBps / 10_000L);
            // Memberships are tenant-funded for now (rider doesn't see a separate fee line).
            // If this needs a rider-paid bps later, mirror the per-product pattern in PassProduct.
            var totalToCharge = tenant.MembershipPriceCents;

            var purchase = new MembershipPurchase
            {
                TenantId = tenant.Id,
                UserId = userId,
                NameAtPurchase = tenant.MembershipName,
                PriceCents = tenant.MembershipPriceCents,
                DurationKind = tenant.MembershipDurationKind,
                ValidFromUtc = now,
                ValidToUtc = validTo,
                AmountCents = totalToCharge,
                ServiceChargeCents = serviceCharge,
                Status = "pending",
                PaymentMethod = "stripe",
            };
            purchase.Id = await _memberships.Create(purchase);

            var metadata = new Dictionary<string, string>
            {
                ["tenant_id"] = tenant.Id.ToString(),
                ["sale_kind"] = "membership",
                ["membership_purchase_id"] = purchase.Id.ToString(),
                ["user_id"] = userId.ToString(),
            };

            // Direct-charge tenants charge on their own connected account; our service fee rides as
            // the Stripe application fee.
            PaymentIntentCreated intent;
            ChargePlan chargePlan;
            try
            {
                chargePlan = _chargeRouter.Plan(tenant, serviceCharge, totalToCharge);
                intent = await _payments.CreatePaymentIntentAsync(
                    amountCents: totalToCharge,
                    currency: "usd",
                    metadata: metadata,
                    receiptEmail: user.Email,
                    connectedAccountId: chargePlan.ConnectedAccountId,
                    applicationFeeCents: chargePlan.ApplicationFeeCents,
                    ct: ct);
            }
            catch (InvalidOperationException ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }

            await _memberships.SetStripePaymentIntentId(purchase.Id, intent.IntentId);
            if (chargePlan.IsDirect)
            {
                await _memberships.MarkDirectCharge(purchase.Id, tenant.Id, chargePlan.ConnectedAccountId!);
            }

            return new ApiResponses().OkResult(new BuyMembershipResponse
            {
                PurchaseId = purchase.Id,
                ClientSecret = intent.ClientSecret,
                AmountCents = totalToCharge,
                RiderServiceChargeCents = 0,
            });
        }

        // Admin: configure the tenant's membership program.
        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpPut("Settings")]
        public async Task<IActionResult> UpdateSettings([FromBody] UpdateMembershipSettingsRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            await _tenants.UpdateMembershipSettings(
                _tenantContext.TenantId,
                req.Enabled,
                req.Name.Trim(),
                req.PriceCents,
                req.DurationKind,
                req.RequiredForRiders,
                req.RequiredForSpectators);
            return new ApiResponses().OkResult();
        }

        // Admin: full purchase history for the tenant (renewals, lapses, etc.).
        [Authorize(Policy = TenantPermissions.Policy.SalesView)]
        [HttpGet("Admin")]
        public async Task<IActionResult> ListForAdmin()
        {
            var rows = await _memberships.ListForTenant(_tenantContext.TenantId);
            // Reuse the history DTO shape — admin doesn't need rider-private info, just the audit list.
            var items = rows.Select(h => new MembershipHistoryItem
            {
                Id = h.Id,
                Name = h.NameAtPurchase,
                DurationKind = h.DurationKind,
                ValidFromUtc = DateTime.SpecifyKind(h.ValidFromUtc, DateTimeKind.Utc),
                ValidToUtc = h.ValidToUtc.HasValue ? DateTime.SpecifyKind(h.ValidToUtc.Value, DateTimeKind.Utc) : null,
                AmountCents = h.AmountCents,
                Status = h.Status,
                CreatedAtUtc = DateTime.SpecifyKind(h.CreatedAt, DateTimeKind.Utc),
            });
            return new ApiResponses().OkResult(items);
        }
    }
}
