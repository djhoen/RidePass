using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Payments;
using Services.Repositories.Interfaces;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    // PUBLIC (no auth): the customer-facing side of a work-order deposit payment request. The
    // emailed link carries an unguessable token; combined with the tenant resolved from the
    // subdomain, that token is the whole credential: it exposes only this one order's summary
    // and lets the customer pay its deposit. Nothing here mutates anything except creating the
    // deposit PaymentIntent; the finalizer webhook books the money.
    [ApiController]
    [Route("api/[controller]")]
    public class ShopDepositController : ControllerBase
    {
        private readonly IBikeShopRepository _shop;
        private readonly IChargeRouter _chargeRouter;
        private readonly IPaymentProvider _payments;
        private readonly ITenantContext _tenantContext;

        public ShopDepositController(IBikeShopRepository shop, IChargeRouter chargeRouter,
            IPaymentProvider payments, ITenantContext tenantContext)
        {
            _shop = shop;
            _chargeRouter = chargeRouter;
            _payments = payments;
            _tenantContext = tenantContext;
        }

        [HttpGet("{token:guid}")]
        public async Task<IActionResult> Get(Guid token)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!_tenantContext.Tenant.BikeShopEnabled)
                return new ApiResponses().NotFoundResult("Not found.");
            var wo = await _shop.GetWorkOrderByDepositToken(token, _tenantContext.TenantId);
            if (wo is null) return new ApiResponses().NotFoundResult("This payment link isn't valid.");
            return new ApiResponses().OkResult(new
            {
                customerName = wo.CustomerName,
                bikeDesc = wo.CustomerBikeDesc,
                status = wo.Status,
                depositCents = wo.DepositCents,
                paid = wo.DepositPaidAt is not null,
                refunded = wo.DepositRefundedAt is not null,
                cancelled = wo.Status == "cancelled",
                // The quote, so the customer sees what they're putting money down on.
                lines = wo.Lines.Select(l => new
                {
                    kind = l.LineKind,
                    description = l.Description,
                    quantity = l.Quantity,
                    unitPriceCents = l.UnitPriceCents,
                }),
            });
        }

        [HttpPost("{token:guid}/Pay")]
        public async Task<IActionResult> Pay(Guid token, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var tenant = _tenantContext.Tenant;
            if (!tenant.BikeShopEnabled) return new ApiResponses().NotFoundResult("Not found.");
            var wo = await _shop.GetWorkOrderByDepositToken(token, _tenantContext.TenantId);
            if (wo is null) return new ApiResponses().NotFoundResult("This payment link isn't valid.");
            if (wo.Status == "cancelled")
                return new ApiResponses().BadRequestResult("This service order was cancelled, so no deposit is due.");
            if (wo.DepositPaidAt is not null)
                return new ApiResponses().BadRequestResult("This deposit has already been paid. You're all set.");
            if (wo.DepositCents < 50)
                return new ApiResponses().BadRequestResult("No deposit is currently due on this order.");

            var metadata = new Dictionary<string, string>
            {
                ["tenant_id"] = _tenantContext.TenantId.ToString(),
                ["sale_kind"] = "shop_wo_deposit",
                ["shop_work_order_id"] = wo.Id.ToString(),
            };
            PaymentIntentCreated intent;
            ChargePlan plan;
            try
            {
                plan = _chargeRouter.Plan(tenant, serviceFeeCents: 0, chargeAmountCents: wo.DepositCents);
                intent = await _payments.CreatePaymentIntentAsync(wo.DepositCents, "usd", metadata, wo.CustomerEmail,
                    connectedAccountId: plan.ConnectedAccountId, applicationFeeCents: plan.ApplicationFeeCents, ct: ct);
            }
            catch (InvalidOperationException ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
            await _shop.SetWorkOrderDepositIntent(wo.Id, _tenantContext.TenantId, intent.IntentId, plan.ConnectedAccountId);
            return new ApiResponses().OkResult(new { clientSecret = intent.ClientSecret, amountCents = wo.DepositCents });
        }
    }
}
