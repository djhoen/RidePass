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
            var sellable = (await _shop.ListProducts(TenantId, activeOnly: true))
                .Where(p => p.IsSellable && p.IsPublished)
                .ToList();
            // Galleries in one grouped query (not folded into ListProducts, which the register
            // and the CSV import also use and neither wants the extra round trip). Carrying them
            // in the catalog payload keeps opening a product's detail view instant.
            var galleries = await _shop.ListImagesForProducts(sellable.Select(p => p.Id), TenantId);
            var products = sellable
                .Select(p => new
                {
                    id = p.Id,
                    name = p.Name,
                    description = p.Description,
                    brand = p.Brand,
                    // Fall back to the first gallery photo so clearing the cover never blanks a card.
                    imageUrl = p.ImageUrl ?? galleries.GetValueOrDefault(p.Id)?.FirstOrDefault()?.ImageUrl,
                    images = (galleries.GetValueOrDefault(p.Id) ?? new())
                        .Select(i => new { id = i.Id, url = i.ImageUrl, caption = i.Caption, sortOrder = i.SortOrder }),
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

        // ── Rentals: browse + book online ────────────────────────────────────────
        // Public rentable catalog: rentable products with a daily rate, trimmed like the sale
        // catalog. Serialized bikes report whether any unit exists on the floor (real availability
        // is per-window, checked at RentalAvailability); pool gear reports free-now-agnostic here.
        [HttpGet("RentalCatalog")]
        public async Task<IActionResult> RentalCatalog()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!_tenantContext.Tenant.BikeShopEnabled)
                return new ApiResponses().OkResult(new { categories = Array.Empty<object>(), products = Array.Empty<object>() });

            var categories = (await _shop.ListCategories(TenantId, activeOnly: true))
                .Select(c => new { id = c.Id, name = c.Name, sortOrder = c.SortOrder });
            var rentable = (await _shop.ListProducts(TenantId, activeOnly: true))
                .Where(p => p.IsRentable && p.IsPublished)
                .ToList();
            var galleries = await _shop.ListImagesForProducts(rentable.Select(p => p.Id), TenantId);
            var products = rentable
                .Select(p => new
                {
                    id = p.Id,
                    name = p.Name,
                    description = p.Description,
                    brand = p.Brand,
                    imageUrl = p.ImageUrl ?? galleries.GetValueOrDefault(p.Id)?.FirstOrDefault()?.ImageUrl,
                    categoryId = p.CategoryId,
                    sortOrder = p.SortOrder,
                    variants = p.Variants
                        .Where(v => v.IsActive && v.DailyRateCents is not null)
                        .Select(v => new
                        {
                            id = v.Id,
                            size = v.Size,
                            color = v.Color,
                            dailyRateCents = v.DailyRateCents,
                            depositCents = v.DepositCents,
                            trackingKind = v.TrackingKind,
                            // Whether any unit exists at all; the per-window count comes from RentalAvailability.
                            onFloor = v.AvailableCount,
                        }),
                })
                .Where(p => p.variants.Any());
            return new ApiResponses().OkResult(new { categories, products });
        }

        // Public per-window availability for one rentable variant. Returns a count only; the
        // specific serialized units are never exposed to customers (the server assigns one at
        // booking). Availability is not PII, so this is anonymous like the catalog.
        [HttpGet("RentalAvailability")]
        public async Task<IActionResult> RentalAvailability([FromQuery] Guid variantId,
            [FromQuery] DateTime startsAt, [FromQuery] DateTime endsAt)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var s = startsAt.ToUniversalTime();
            var e = endsAt.ToUniversalTime();
            if (e <= s) return new ApiResponses().BadRequestResult("The return time must be after the start time.");
            var variant = await _shop.GetVariant(variantId, TenantId);
            if (variant is null || variant.DailyRateCents is null)
                return new ApiResponses().NotFoundResult("That rental isn't available.");
            var available = variant.TrackingKind == "serialized"
                ? (await _shop.GetFreeSerializedUnits(variantId, TenantId, s, e)).Count
                : await _shop.GetPoolAvailability(variantId, TenantId, s, e);
            var days = Math.Max(1, (int)Math.Ceiling((e - s).TotalDays));
            return new ApiResponses().OkResult(new
            {
                available,
                days,
                dailyRateCents = variant.DailyRateCents,
                depositCents = variant.DepositCents,
                lineRateCents = variant.DailyRateCents.Value * days,
            });
        }

        // Book a rental online as the signed-in rider. Mirrors the counter Book (same re-pricing,
        // season-pass discount, service fee, tax, deposit hold) but takes identity from the token,
        // is always card, always holds the deposit, and auto-assigns free serialized units so the
        // customer never picks a serial number. Settles through the same shop_rental webhook.
        [Authorize]
        [HttpPost("BookRental")]
        public async Task<IActionResult> BookRental([FromBody] RentalCustomerBookRequest req, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var tenant = _tenantContext.Tenant;
            if (!tenant.BikeShopEnabled)
                return new ApiResponses().BadRequestResult("The bike shop isn't available at this track.");
            if (!Guid.TryParse(User.FindFirst("UserId")?.Value, out var userId))
                return new ApiResponses().BadRequestResult("Not signed in.");
            var user = await _users.GetById(userId);
            if (user is null) return new ApiResponses().BadRequestResult("User not found.");

            var startsAt = req.StartsAt.ToUniversalTime();
            var endsAt = req.EndsAt.ToUniversalTime();
            if (endsAt <= startsAt) return new ApiResponses().BadRequestResult("The return time must be after the start time.");
            if (startsAt < DateTime.UtcNow.AddHours(-1))
                return new ApiResponses().BadRequestResult("The rental window starts in the past.");

            var days = Math.Max(1, (int)Math.Ceiling((endsAt - startsAt).TotalDays));
            var infos = (await _shop.GetVariantsForSale(req.Lines.Select(l => l.VariantId), TenantId))
                .ToDictionary(v => v.Id);

            var lines = new List<ShopRentalLine>();
            int amount = 0, depositTotal = 0, riders = 0;
            foreach (var line in req.Lines)
            {
                var variant = await _shop.GetVariant(line.VariantId, TenantId);
                if (variant is null || !infos.ContainsKey(line.VariantId))
                    return new ApiResponses().BadRequestResult("An item in your booking is no longer available.");
                if (variant.DailyRateCents is null)
                    return new ApiResponses().BadRequestResult($"\"{infos[line.VariantId].ProductName}\" isn't set up for rental.");
                var info = infos[line.VariantId];
                var label = string.Join(" / ", new[] { info.Size, info.Color, info.Gender }.Where(s => !string.IsNullOrWhiteSpace(s)));
                var perDay = variant.DailyRateCents.Value;

                if (variant.TrackingKind == "serialized")
                {
                    // Auto-assign: pick the first `quantity` free units for the window so the
                    // customer never sees serial numbers. One rental line per assigned unit.
                    var free = await _shop.GetFreeSerializedUnits(line.VariantId, TenantId, startsAt, endsAt);
                    if (free.Count < line.Quantity)
                        return new ApiResponses().BadRequestResult(
                            $"Only {free.Count} of \"{info.ProductName}\" free for those dates. Pick fewer or a different window.");
                    foreach (var unit in free.Take(line.Quantity))
                    {
                        var lineAmount = perDay * days;
                        amount += lineAmount;
                        depositTotal += variant.DepositCents;
                        riders += 1;
                        lines.Add(new ShopRentalLine
                        {
                            VariantId = line.VariantId,
                            ItemId = unit.Id,
                            Quantity = 1,
                            NameSnapshot = info.ProductName,
                            VariantLabel = string.IsNullOrWhiteSpace(label) ? null : label,
                            DailyRateCentsFrozen = perDay,
                            DepositCentsFrozen = variant.DepositCents,
                            LineAmountCents = lineAmount,
                        });
                    }
                }
                else
                {
                    var available = await _shop.GetPoolAvailability(line.VariantId, TenantId, startsAt, endsAt);
                    if (line.Quantity > available)
                        return new ApiResponses().BadRequestResult(
                            $"Only {available} of \"{info.ProductName}\" free for those dates. Pick fewer or a different window.");
                    var lineAmount = perDay * days * line.Quantity;
                    amount += lineAmount;
                    depositTotal += variant.DepositCents * line.Quantity;
                    riders += line.Quantity;
                    lines.Add(new ShopRentalLine
                    {
                        VariantId = line.VariantId,
                        ItemId = null,
                        Quantity = line.Quantity,
                        NameSnapshot = info.ProductName,
                        VariantLabel = string.IsNullOrWhiteSpace(label) ? null : label,
                        DailyRateCentsFrozen = perDay,
                        DepositCentsFrozen = variant.DepositCents,
                        LineAmountCents = lineAmount,
                    });
                }
            }

            // Season-pass rental benefit (valid on the START date), same as the counter.
            var benefitDiscount = 0;
            if (amount > 0)
            {
                var grants = await _seasonPasses.ListActiveBenefitGrantsForUser(
                    userId, TenantId, benefitType: "rental", scopeId: null, onDateUtc: startsAt);
                benefitDiscount = grants.Count == 0 ? 0 : grants.Max(g => g.Benefit.DiscountFor(amount));
            }
            var netRental = amount - benefitDiscount;

            // Optional damage waiver ("insurance"): a non-refundable add-on = rate * gross rental
            // value. When taken it waives the refundable deposit. It rides in the rental subtotal so
            // it is fee'd and taxed like the rest of the rental.
            //
            // Service fee + tax, identical to the counter, and literally so: both go through
            // RentalCharge, which owns the fee base, the waiver rate, and the deposit waiver. The
            // deposit is never in either base.
            var insuranceOffered = tenant.RentalInsuranceEnabled && tenant.RentalInsuranceBps > 0;
            var insuranceCents = Services.Payments.RentalCharge.InsuranceFor(
                amount, tenant.RentalInsuranceBps, insuranceOffered, req.Insurance);
            var charge = Services.Payments.RentalCharge.WithInsurance(
                netRental, insuranceCents, tenant.ServiceChargeBps,
                tenant.RentalRiderPaidServiceChargeBps, depositTotal);

            var subtotal = netRental + insuranceCents;
            var depositCents = charge.DepositCents;
            var serviceChargeCents = charge.ServiceChargeCents;
            var renterFeeCents = charge.RiderServiceChargeCents;
            var taxableBase = subtotal + (tenant.RentalTaxServiceChargeTaxable ? renterFeeCents : 0);
            var taxCents = (int)Math.Round(
                (decimal)taxableBase * (tenant.RentalTaxBps ?? 0) / 10_000m, MidpointRounding.AwayFromZero);
            var total = subtotal + renterFeeCents + taxCents;

            if (total < 50) return new ApiResponses().BadRequestResult("An online rental must total at least 50 cents.");
            if (tenant.StripeChargeMode == "direct" && string.IsNullOrEmpty(tenant.StripeConnectAccountId))
                return new ApiResponses().BadRequestResult("This track can't take online payments yet.");

            var rental = new ShopRental
            {
                TenantId = TenantId,
                RenterUserId = userId,
                RenterName = $"{user.FirstName} {user.LastName}".Trim(),
                RenterEmail = user.Email,
                RenterPhone = user.Phone,
                StartsAt = startsAt,
                EndsAt = endsAt,
                Status = "pending",
                AmountCents = amount + insuranceCents,
                InsuranceCents = insuranceCents,
                InsuranceLabelSnapshot = insuranceCents > 0
                    ? (string.IsNullOrWhiteSpace(tenant.RentalInsuranceLabel)
                        ? "Damage Protection" : tenant.RentalInsuranceLabel.Trim())
                    : null,
                TaxCents = taxCents,
                TotalCents = total,
                ServiceChargeCents = serviceChargeCents,
                RidersRequired = Math.Max(1, riders),
                DepositCents = depositCents,
                PaymentMethod = "stripe",
                SoldByUserId = null,   // self-serve online booking; no counter operator
            };
            var (rentalId, receipt) = await _shop.CreateRental(rental, lines);

            var metadata = new Dictionary<string, string>
            {
                ["tenant_id"] = TenantId.ToString(),
                ["sale_kind"] = "shop_rental",
                ["shop_rental_id"] = rentalId.ToString(),
            };
            string? depositClientSecret = null;
            PaymentIntentCreated intent;
            ChargePlan plan;
            try
            {
                plan = _chargeRouter.Plan(tenant, serviceFeeCents: serviceChargeCents, chargeAmountCents: total);
                intent = await _payments.CreatePaymentIntentAsync(total, "usd", metadata, rental.RenterEmail,
                    connectedAccountId: plan.ConnectedAccountId, applicationFeeCents: plan.ApplicationFeeCents, ct: ct);
                await _shop.SetRentalPaymentIntent(rentalId, intent.IntentId);
                if (plan.IsDirect) await _shop.MarkRentalDirectCharge(rentalId, TenantId, plan.ConnectedAccountId!);

                if (depositCents > 0)
                {
                    var holdMeta = new Dictionary<string, string>(metadata) { ["sale_kind"] = "shop_rental_deposit_hold" };
                    var hold = await _payments.CreateHoldPaymentIntentAsync(depositCents, "usd", holdMeta,
                        rental.RenterEmail, connectedAccountId: plan.ConnectedAccountId, ct: ct);
                    await _shop.SetRentalDepositIntent(rentalId, hold.IntentId);
                    depositClientSecret = hold.ClientSecret;
                }
            }
            catch (InvalidOperationException ex)
            {
                await _shop.MarkRentalFailed(rentalId);
                return new ApiResponses().BadRequestResult(ex.Message);
            }

            return new ApiResponses().OkResult(new
            {
                rentalId, receiptToken = receipt, status = "pending",
                clientSecret = intent.ClientSecret, depositClientSecret,
                subtotalCents = subtotal, feeCents = renterFeeCents, taxCents,
                totalCents = total, depositCents = depositTotal, days,
            });
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

        /// <summary>
        /// The rider's own shop orders (My Orders). The order number is the proof of purchase:
        /// it is what the confirmation email tells them to show and what the counter searches by.
        /// </summary>
        [Authorize]
        [HttpGet("MyOrders")]
        public async Task<IActionResult> MyOrders()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!Guid.TryParse(User.FindFirst("UserId")?.Value, out var userId))
                return new ApiResponses().BadRequestResult("Invalid token.");

            var orders = await _shop.ListSalesForBuyer(TenantId, userId, 50);
            return new ApiResponses().OkResult(orders.Select(ToMyOrder));
        }

        /// <summary>
        /// One of the rider's own orders. Used right after a card payment to pick up the order
        /// number once the webhook has settled the sale (MyOrders can't serve that: it hides
        /// pending rows, so it cannot tell "still settling" apart from "does not exist").
        /// </summary>
        [Authorize]
        [HttpGet("Order/{saleId:guid}")]
        public async Task<IActionResult> OrderStatus(Guid saleId)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!Guid.TryParse(User.FindFirst("UserId")?.Value, out var userId))
                return new ApiResponses().BadRequestResult("Invalid token.");

            var sale = await _shop.GetSale(saleId, TenantId);
            // Someone else's order is reported as missing rather than forbidden: a guessable id
            // must not confirm that an order exists. Ownership is the authorization here.
            if (sale is null || sale.BuyerUserId != userId)
                return new ApiResponses().NotFoundResult("Order not found.");
            return new ApiResponses().OkResult(ToMyOrder(sale));
        }

        private static object ToMyOrder(Services.Repositories.Data.BikeShopData.ShopSaleWithLines o) => new
        {
            saleId = o.Id,
            orderNumber = o.OrderNumber,
            status = o.Status,
            orderChannel = o.OrderChannel,
            // SpecifyKind matters: the client parses these as UTC before converting to the
            // tenant's timezone, and a Kind-unspecified value serializes without the Z.
            pickedUpAtUtc = o.PickedUpAt is null ? null : (DateTime?)DateTime.SpecifyKind(o.PickedUpAt.Value, DateTimeKind.Utc),
            createdAtUtc = DateTime.SpecifyKind(o.CreatedAt, DateTimeKind.Utc),
            totalCents = o.TotalCents,
            creditAppliedCents = o.CreditAppliedCents,
            lines = o.Lines.Select(l => new
            {
                name = l.NameSnapshot,
                variantLabel = l.VariantLabel,
                quantity = l.Quantity,
                unitPriceCents = l.UnitPriceCents,
            }),
        };
    }
}
