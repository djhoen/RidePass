using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Payments;
using Services.Repositories.Data.BikeShopData;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Interfaces;
using webapi.Controllers.API.Data.BikeShop;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    // The rider-facing shop storefront: a PUBLIC browsable catalog and a signed-in buy-online /
    // pick-up-in-store checkout. Online orders are ordinary shop_sale rows (channel 'online'),
    // so the payment webhook, stock depletion, order numbers, ledger, store credit, and loyalty
    // all ride the existing machinery. Serialized products (bikes) are browse-only online: a
    // specific unit is chosen and sold at the counter.
    [ApiController]
    [Route("api/[controller]")]
    public class ShopStoreController : ControllerBase
    {
        private readonly IBikeShopRepository _shop;
        private readonly ITenantCreditRepository _credit;
        private readonly ICouponRepository _coupons;
        private readonly Services.Coupons.ICouponValidator _couponValidator;
        private readonly ISeasonPassRepository _seasonPasses;
        private readonly IUserRepository _users;
        private readonly IChargeRouter _chargeRouter;
        private readonly IPaymentProvider _payments;
        private readonly ITenantContext _tenantContext;

        public ShopStoreController(IBikeShopRepository shop, ITenantCreditRepository credit,
            ICouponRepository coupons, Services.Coupons.ICouponValidator couponValidator,
            ISeasonPassRepository seasonPasses, IUserRepository users,
            IChargeRouter chargeRouter, IPaymentProvider payments, ITenantContext tenantContext)
        {
            _shop = shop;
            _credit = credit;
            _coupons = coupons;
            _couponValidator = couponValidator;
            _seasonPasses = seasonPasses;
            _users = users;
            _chargeRouter = chargeRouter;
            _payments = payments;
            _tenantContext = tenantContext;
        }

        private Guid TenantId => _tenantContext.TenantId;

        // Public catalog: active products with a deliberately trimmed projection (no costs, no
        // thresholds, no supplier wiring). Pool variants carry live availability; serialized
        // ones just say whether any unit is on the floor.
        [HttpGet("Catalog")]
        public async Task<IActionResult> Catalog()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!_tenantContext.Tenant.BikeShopEnabled)
                return new ApiResponses().OkResult(new { categories = Array.Empty<object>(), products = Array.Empty<object>() });

            var categories = (await _shop.ListCategories(TenantId, activeOnly: true))
                .Select(c => new { id = c.Id, name = c.Name, sortOrder = c.SortOrder });
            var products = (await _shop.ListProducts(TenantId, activeOnly: true))
                .Where(p => p.IsSellable && p.IsPublished)
                .Select(p => new
                {
                    id = p.Id,
                    name = p.Name,
                    description = p.Description,
                    brand = p.Brand,
                    imageUrl = p.ImageUrl,
                    categoryId = p.CategoryId,
                    sortOrder = p.SortOrder,
                    variants = p.Variants
                        .Where(v => v.IsActive && v.SalePriceCents is not null)
                        .Select(v => new
                        {
                            id = v.Id,
                            size = v.Size,
                            color = v.Color,
                            salePriceCents = v.SalePriceCents,
                            trackingKind = v.TrackingKind,
                            available = v.AvailableCount,
                        }),
                })
                .Where(p => p.variants.Any());
            return new ApiResponses().OkResult(new { categories, products });
        }

        // Buy online, pick up in store. Signed-in riders only (the account carries the pickup
        // identity, pass benefits, and store credit). Pool variants only; the server re-prices
        // everything and the finalizer settles it exactly like a counter card sale.
        [Authorize]
        [HttpPost("Order")]
        public async Task<IActionResult> Order([FromBody] ShopOnlineOrderRequest req, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var tenant = _tenantContext.Tenant;
            if (!tenant.BikeShopEnabled)
                return new ApiResponses().BadRequestResult("The shop isn't available at this track.");
            if (!Guid.TryParse(User.FindFirst("UserId")?.Value, out var userId))
                return new ApiResponses().BadRequestResult("Not signed in.");
            var user = await _users.GetById(userId);
            if (user is null) return new ApiResponses().BadRequestResult("User not found.");
            if (req.Lines.Count == 0) return new ApiResponses().BadRequestResult("The cart is empty.");

            var infos = (await _shop.GetVariantsForSale(req.Lines.Select(l => l.VariantId), TenantId))
                .ToDictionary(v => v.Id);
            var wantByVariant = req.Lines.GroupBy(l => l.VariantId)
                .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));

            var saleLines = new List<ShopSaleLine>();
            var subtotal = 0;
            foreach (var line in req.Lines)
            {
                if (!infos.TryGetValue(line.VariantId, out var info) || info.SalePriceCents is null)
                    return new ApiResponses().BadRequestResult("An item in your cart is no longer available. Refresh the shop and try again.");
                if (info.TrackingKind != "pool")
                    return new ApiResponses().BadRequestResult(
                        $"\"{info.ProductName}\" is sold in store so we can set you up with the right unit. Everything else can check out online.");
                if (wantByVariant[line.VariantId] > info.Available)
                    return new ApiResponses().BadRequestResult(
                        $"Only {info.Available} of \"{info.ProductName}\" left in stock. Adjust the quantity and try again.");

                var unit = info.SalePriceCents.Value;
                subtotal += unit * line.Quantity;
                saleLines.Add(new ShopSaleLine
                {
                    VariantId = line.VariantId,
                    Quantity = line.Quantity,
                    NameSnapshot = info.ProductName,
                    VariantLabel = string.Join(" / ", new[] { info.Size, info.Color, info.Gender }.Where(s => !string.IsNullOrWhiteSpace(s))),
                    UnitPriceCents = unit,
                    TaxRateBps = info.TaxRateBps,
                    UnitCostCentsFrozen = info.CostCents,
                });
            }

            // Discounts mirror the register: the buyer's pass benefit first, then a coupon on
            // what's left, spread across lines by price share so per-line tax is on the net.
            var benefitDiscount = 0;
            if (subtotal > 0)
            {
                var grants = await _seasonPasses.ListActiveBenefitGrantsForUser(
                    userId, TenantId, benefitType: "retail", scopeId: null, onDateUtc: DateTime.UtcNow);
                benefitDiscount = grants.Count == 0 ? 0 : grants.Max(g => g.Benefit.DiscountFor(subtotal));
            }
            Services.Repositories.Data.CouponData.CouponApplication? couponApp = null;
            if (!string.IsNullOrWhiteSpace(req.CouponCode))
            {
                var v = await _couponValidator.ValidateAsync(TenantId, req.CouponCode!,
                    scope: "shop", eventId: null, subtotalCents: subtotal - benefitDiscount, userId: userId);
                if (v.error is not null) return new ApiResponses().BadRequestResult(v.error);
                couponApp = v.application;
            }
            var discountTotal = Math.Min(subtotal, benefitDiscount + (couponApp?.DiscountCents ?? 0));
            if (discountTotal > 0)
            {
                var handedOut = 0;
                for (var i = 0; i < saleLines.Count; i++)
                {
                    var l = saleLines[i];
                    var lineBase = l.UnitPriceCents * l.Quantity;
                    l.DiscountCents = i == saleLines.Count - 1
                        ? discountTotal - handedOut
                        : (int)((long)discountTotal * lineBase / subtotal);
                    handedOut += l.DiscountCents;
                }
            }
            var taxTotal = 0;
            foreach (var l in saleLines)
            {
                var net = Math.Max(0, l.UnitPriceCents * l.Quantity - l.DiscountCents);
                l.TaxCents = l.TaxRateBps > 0
                    ? (int)Math.Round(net * l.TaxRateBps / 10000.0, MidpointRounding.AwayFromZero) : 0;
                taxTotal += l.TaxCents;
            }
            var total = subtotal - discountTotal + taxTotal;

            // Store credit as the last tender, resolved strictly by the signed-in user.
            Services.Repositories.Data.CreditData.TenantCreditAccount? creditAccount = null;
            var creditApplied = 0;
            if (req.CreditCents > 0 && total > 0)
            {
                creditAccount = await _credit.GetAccountForUser(TenantId, userId);
                if (creditAccount is not null && creditAccount.BalanceCents > 0)
                    creditApplied = Math.Min(Math.Min(req.CreditCents, creditAccount.BalanceCents), total);
            }
            var due = total - creditApplied;
            if (due > 0 && due < 50)
                return new ApiResponses().BadRequestResult(
                    "Less than 50 cents would be left to charge after credit. Adjust the cart or keep the credit for next time.");
            if (due > 0 && tenant.StripeChargeMode == "direct" && string.IsNullOrEmpty(tenant.StripeConnectAccountId))
                return new ApiResponses().BadRequestResult("This track can't take online payments yet.");

            var sale = new ShopSale
            {
                TenantId = TenantId,
                BuyerUserId = userId,
                BuyerEmail = user.Email,
                BuyerName = $"{user.FirstName} {user.LastName}".Trim(),
                Status = "pending",
                SubtotalCents = subtotal,
                DiscountCents = discountTotal,
                TaxCents = taxTotal,
                TotalCents = total,
                CreditAppliedCents = creditApplied,
                CreditAccountId = creditApplied > 0 ? creditAccount!.Id : null,
                PricesIncludeTax = false,
                PaymentMethod = due > 0 ? "stripe" : "cash",
                OrderChannel = "online",
            };
            var (saleId, receipt) = await _shop.CreateSale(sale, saleLines);

            if (creditApplied > 0 &&
                !await _credit.TryAdjust(creditAccount!.Id, TenantId, -creditApplied, "redeem", "shop_sale", saleId, null, userId))
            {
                await _shop.MarkSaleFailed(saleId);
                return new ApiResponses().BadRequestResult("Your credit balance just changed. Reload and try again.");
            }
            if (couponApp is not null)
            {
                await _coupons.RecordRedemption(new Services.Repositories.Data.CouponData.CouponRedemption
                {
                    CouponId = couponApp.Coupon.Id,
                    TenantId = TenantId,
                    UserId = userId,
                    SourceKind = "shop_sale",
                    SourceId = saleId,
                    DiscountCents = couponApp.DiscountCents,
                });
            }

            // Fully covered by credit: settle now (no PI). The order number is the pickup claim.
            if (due == 0)
            {
                if (await _shop.TryMarkSalePaid(saleId, TenantId))
                {
                    var orderNumber = await _shop.NextOrderNumber(TenantId);
                    await _shop.SetSaleOrderNumber(saleId, orderNumber);
                    try { await _shop.DepleteForSale(saleId, TenantId, null); }
                    catch { /* inventory depletion is best-effort; the sale is paid regardless */ }
                    return new ApiResponses().OkResult(new
                    {
                        saleId, receiptToken = receipt, status = "paid", orderNumber,
                        totalCents = total, creditAppliedCents = creditApplied, dueCents = 0,
                    });
                }
                return new ApiResponses().OkResult(new { saleId, receiptToken = receipt, status = "paid",
                    totalCents = total, creditAppliedCents = creditApplied, dueCents = 0 });
            }

            var metadata = new Dictionary<string, string>
            {
                ["tenant_id"] = TenantId.ToString(),
                ["sale_kind"] = "shop_sale",
                ["shop_sale_id"] = saleId.ToString(),
            };
            PaymentIntentCreated intent;
            ChargePlan plan;
            try
            {
                plan = _chargeRouter.Plan(tenant, serviceFeeCents: 0, chargeAmountCents: due);
                intent = await _payments.CreatePaymentIntentAsync(due, "usd", metadata, user.Email,
                    connectedAccountId: plan.ConnectedAccountId, applicationFeeCents: plan.ApplicationFeeCents, ct: ct);
            }
            catch (InvalidOperationException ex)
            {
                await _shop.MarkSaleFailed(saleId);
                await _credit.ReverseRedeem(TenantId, "shop_sale", saleId, "payment could not start");
                return new ApiResponses().BadRequestResult(ex.Message);
            }
            await _shop.SetSalePaymentIntent(saleId, intent.IntentId);
            if (plan.IsDirect) await _shop.MarkSaleDirectCharge(saleId, TenantId, plan.ConnectedAccountId!);

            return new ApiResponses().OkResult(new
            {
                saleId, receiptToken = receipt, status = "pending", clientSecret = intent.ClientSecret,
                totalCents = total, creditAppliedCents = creditApplied, dueCents = due,
            });
        }
    }
}
