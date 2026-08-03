using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Payments;
using Services.Repositories.Data.BikeShopData;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Interfaces;
using Services.Helpers;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.BikeShop;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    // The retail register: ring up a cart of catalog variants and take payment. Separate from the
    // catalog-admin controller (and from the concessions register) but reuses the same charge-routing
    // and ledger machinery. Server prices everything from the catalog; the client never sends amounts.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]   // authenticated; each action adds its own permission (counter vs refund)
    public class BikeShopRegisterController : ControllerBase
    {
        private readonly IBikeShopRepository _shop;
        private readonly Services.Audit.IAuditLogger _audit;
        private readonly IDiscountPresetRepository _discounts;
        private readonly webapi.Security.IManagerPinService _managerPin;
        private readonly IChargeRouter _chargeRouter;
        private readonly IPaymentProvider _payments;
        private readonly IFeeCalculator _feeCalculator;
        private readonly ITenantLedgerRepository _ledger;
        private readonly Services.Coupons.ICouponValidator _couponValidator;
        private readonly ICouponRepository _coupons;
        private readonly Services.Pricing.ISeasonPassPerkResolver _perks;
        private readonly IUserRepository _users;
        private readonly Services.Notifications.INotificationService _notifications;
        private readonly ISmtpEmailer _emailer;
        private readonly ISmsSender _sms;
        private readonly ITenantCreditRepository _credit;
        private readonly IGiftCardRepository _giftCards;
        private readonly Services.GiftCards.IGiftCardValidator _giftCardValidator;
        private readonly ITenantRepository _tenants;
        private readonly ITenantContext _tenantContext;
        private readonly IPlatformPartRepository _platformParts;
        private readonly Services.BikeShop.IPartLookupProvider _partLookup;
        private readonly ILogger<BikeShopRegisterController> _logger;

        public BikeShopRegisterController(IBikeShopRepository shop, IChargeRouter chargeRouter,
            IPaymentProvider payments, IFeeCalculator feeCalculator, ITenantLedgerRepository ledger,
            Services.Coupons.ICouponValidator couponValidator, ICouponRepository coupons,
            IUserRepository users,
            Services.Pricing.ISeasonPassPerkResolver perks,
            Services.Notifications.INotificationService notifications,
            ISmtpEmailer emailer, ISmsSender sms, ITenantCreditRepository credit,
            IGiftCardRepository giftCards, Services.GiftCards.IGiftCardValidator giftCardValidator,
            ITenantRepository tenants,
            ITenantContext tenantContext,
            IPlatformPartRepository platformParts,
            Services.BikeShop.IPartLookupProvider partLookup,
            ILogger<BikeShopRegisterController> logger,
            Services.Audit.IAuditLogger audit,
            IDiscountPresetRepository discounts,
            webapi.Security.IManagerPinService managerPin)
        {
            _platformParts = platformParts;
            _partLookup = partLookup;
            _logger = logger;
            _audit = audit;
            _discounts = discounts;
            _managerPin = managerPin;
            _emailer = emailer;
            _sms = sms;
            _credit = credit;
            _giftCards = giftCards;
            _giftCardValidator = giftCardValidator;
            _tenants = tenants;
            _shop = shop;
            _chargeRouter = chargeRouter;
            _payments = payments;
            _feeCalculator = feeCalculator;
            _ledger = ledger;
            _couponValidator = couponValidator;
            _coupons = coupons;
            _perks = perks;
            _users = users;
            _notifications = notifications;
            _tenantContext = tenantContext;
        }

        private Guid TenantId => _tenantContext.TenantId;
        private Guid? UserId => Guid.TryParse(User.FindFirst("UserId")?.Value, out var id) ? id : null;

        /// <summary>
        /// What did the cashier just scan? One code in, at most one variant out.
        ///
        /// This replaces matching in the browser against the whole catalog. That only ever found
        /// products already downloaded, needed the entire catalog client-side to work at all, and
        /// compared the code as a raw string: a part stored as a 12-digit UPC-A did not match the
        /// same part scanned as a 13-digit EAN, which is a thing scanners routinely do.
        ///
        /// The code is normalised to a GTIN-14 here (Services.BikeShop.Gtin) and matched on that
        /// first, then on SKU, then MPN. A code that is not a valid GTIN is not a failure: shop
        /// SKUs are scanned from the shop's own labels, so it simply falls through.
        ///
        /// On a miss it falls through to the shared parts library, so a barcode this shop has
        /// never carried can still come back with a name instead of a shrug. See Script0248 for
        /// why that library is safe to share across tenants.
        /// </summary>
        [Authorize(Policy = TenantPermissions.Policy.ShopCounter)]
        [HttpGet("Resolve")]
        public async Task<IActionResult> Resolve([FromQuery] string code, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var trimmed = code?.Trim() ?? string.Empty;
            if (trimmed.Length == 0) return new ApiResponses().BadRequestResult("Scan or type a code.");

            var gtin14 = Services.BikeShop.Gtin.Normalize(trimmed);
            var match = await _shop.ResolveVariantByCode(TenantId, trimmed, gtin14);

            if (match is null)
            {
                // A miss is a normal outcome at a counter, not an error: the part may be new. 200
                // with found=false lets the register offer "add this product" instead of making
                // the cashier read an error and start again. `gtin14` rides back so that form can
                // be prefilled with a barcode we already know is well-formed.
                var suggestion = gtin14 is null ? null : await LookUpSharedLibrary(gtin14, ct);
                return new ApiResponses().OkResult(new
                {
                    found = false,
                    scanned = trimmed,
                    gtin14,
                    // Distinguishes "a real barcode we've never seen" from "someone typed a word":
                    // the first is worth offering to create, the second is probably a typo.
                    isValidBarcode = gtin14 is not null,
                    // Identity only. Whatever this shop charges for the part is this shop's
                    // business, so there is deliberately no price here to prefill.
                    suggestion = suggestion is null ? null : new
                    {
                        name = suggestion.Name,
                        brand = suggestion.Brand,
                        mpn = suggestion.Mpn,
                        categoryHint = suggestion.CategoryHint,
                        // Lets the UI say how much to trust it: one shop's word reads differently
                        // from nine shops independently agreeing.
                        timesConfirmed = suggestion.TimesConfirmed,
                    },
                });
            }

            // This shop's own catalog just said what this barcode is, which is the strongest
            // signal the library can get: a real part, in real use, at a real counter. Contribute
            // it so the next shop to scan the same code gets a name instead of nothing.
            await ContributeToSharedLibrary(match, gtin14);

            return new ApiResponses().OkResult(new
            {
                found = true,
                scanned = trimmed,
                variantId = match.Id,
                productId = match.ProductId,
                productName = match.ProductName,
                match.Sku,
                match.Barcode,
                match.Gtin14,
                match.Size,
                match.Color,
                match.SalePriceCents,
                match.TrackingKind,
                match.AvailableCount,
            });
        }

        /// <summary>
        /// Layers 2 and 4 of the resolver: the shared library, then an external GTIN vendor for a
        /// code nobody on the platform has ever seen. A vendor answer is cached into the library
        /// tagged with its source slug, so honouring a "delete our data" clause stays one call to
        /// PurgeSource. No vendor ships enabled today; see IPartLookupProvider for why.
        /// </summary>
        private async Task<PlatformPart?> LookUpSharedLibrary(string gtin14, CancellationToken ct)
        {
            try
            {
                var known = await _platformParts.GetByGtin(gtin14);
                if (known is not null || !_partLookup.IsEnabled) return known;

                var found = await _partLookup.Lookup(gtin14, ct);
                if (found is null) return null;

                // Deliberately NOT via Confirm(): a vendor's answer is not a shop confirming
                // anything, so it must not inflate times_confirmed, and its source has to name the
                // vendor rather than claim to be ours.
                return await _platformParts.CacheFromVendor(_partLookup.SourceSlug, found);
            }
            catch (Exception ex)
            {
                // The library is an enhancement to a scan, never a dependency of one. If it is
                // down, the cashier gets the plain "not in your catalog" answer and carries on.
                _logger.LogWarning(ex, "Shared parts library lookup failed for {Gtin14}", gtin14);
                return null;
            }
        }

        /// <summary>
        /// Records this shop's own catalog as confirming what a barcode means. Identity only, and
        /// idempotent per tenant, so a busy counter scanning one tube all day counts once.
        ///
        /// CONTRIBUTES THE MANUFACTURER'S NAME, NEVER THE SHOP'S. match.ProductName is what this
        /// shop calls the thing, it is tenant-authored free text, and it must never reach a table
        /// another tenant reads. Only ManufacturerName crosses the line, and a variant without one
        /// contributes nothing at all rather than falling back.
        /// </summary>
        private async Task ContributeToSharedLibrary(ShopScanMatch match, string? gtin14)
        {
            // What may be shared, if anything. The rule is a tested pure function rather than an
            // if-statement here so that reintroducing a fallback to the shop's own product name
            // fails a test instead of quietly leaking it. See Services.BikeShop.LibraryContribution.
            var manufacturerName = Services.BikeShop.LibraryContribution
                .NameToContribute(gtin14, match.ManufacturerName, match.ManufacturerNameSource);
            if (manufacturerName is null) return;

            // Already linked, so this shop has confirmed this part before and there is nothing to
            // add. Without this the busiest path in the register pays two writes on EVERY scan
            // just to re-assert something already true. Goes back to null if the shared entry is
            // ever purged, which correctly makes the next scan contribute again.
            if (match.PlatformPartId is not null) return;
            try
            {
                var partId = await _platformParts.Confirm(TenantId, gtin14, manufacturerName,
                    brand: null, mpn: match.Mpn, categoryHint: null);
                await _shop.LinkVariantToPlatformPart(match.Id, TenantId, partId);
            }
            catch (Exception ex)
            {
                // Contributing is a side effect of a scan, not its purpose. Never fail a sale
                // because the library write did.
                _logger.LogWarning(ex, "Could not contribute {Gtin14} to the shared parts library", gtin14);
            }
        }

        [Authorize(Policy = TenantPermissions.Policy.ShopCounter)]
        [HttpPost("Sale")]
        public async Task<IActionResult> RingUp([FromBody] RingUpShopSaleRequest req, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (req.Lines.Count == 0) return new ApiResponses().BadRequestResult("The cart is empty.");
            if (!_tenantContext.Tenant.BikeShopEnabled)
                return new ApiResponses().BadRequestResult("The bike shop isn't turned on for this track.");

            // No shop-specific tax-inclusive setting yet; retail prices are entered pre-tax.
            const bool pricesIncludeTax = false;

            var infos = (await _shop.GetVariantsForSale(req.Lines.Select(l => l.VariantId), TenantId))
                .ToDictionary(v => v.Id);

            // Requested quantity per variant, to check against available stock as a whole (the same
            // variant may appear on more than one line).
            var wantByVariant = req.Lines.GroupBy(l => l.VariantId)
                .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));

            var saleLines = new List<ShopSaleLine>();
            var usedItemIds = new HashSet<Guid>();
            int subtotal = 0, taxTotal = 0;

            foreach (var line in req.Lines)
            {
                if (!infos.TryGetValue(line.VariantId, out var info))
                    return new ApiResponses().BadRequestResult("An item in the cart is no longer available.");
                if (info.SalePriceCents is null)
                    return new ApiResponses().BadRequestResult($"\"{info.ProductName}\" isn't set up for sale.");

                // COGS snapshot for the margin report: a serialized unit's own acquired cost when
                // it has one, else the variant's cost.
                var unitCost = info.CostCents;
                if (info.TrackingKind == "serialized")
                {
                    if (line.ItemId is null)
                        return new ApiResponses().BadRequestResult($"Pick which \"{info.ProductName}\" unit is being sold.");
                    if (line.Quantity != 1)
                        return new ApiResponses().BadRequestResult("Serialized items are sold one unit per line.");
                    if (!usedItemIds.Add(line.ItemId.Value))
                        return new ApiResponses().BadRequestResult("The same unit is on the cart twice.");
                    var item = await _shop.GetItem(line.ItemId.Value, TenantId);
                    if (item is null || item.VariantId != line.VariantId || item.Status != "available")
                        return new ApiResponses().BadRequestResult($"That \"{info.ProductName}\" unit isn't available to sell.");
                    unitCost = item.AcquiredCostCents ?? info.CostCents;
                }
                else if (wantByVariant[line.VariantId] > info.Available)
                {
                    return new ApiResponses().BadRequestResult(
                        $"Only {info.Available} of \"{info.ProductName}\" in stock.");
                }

                var unit = info.SalePriceCents.Value;
                subtotal += unit * line.Quantity;

                // Tax + discount are filled in after discounts resolve (tax applies to the net).
                saleLines.Add(new ShopSaleLine
                {
                    VariantId = line.VariantId,
                    ItemId = info.TrackingKind == "serialized" ? line.ItemId : null,
                    Quantity = line.Quantity,
                    NameSnapshot = info.ProductName,
                    VariantLabel = VariantLabel(info),
                    UnitPriceCents = unit,
                    TaxRateBps = info.TaxRateBps,
                    UnitCostCentsFrozen = unitCost,
                });
            }

            // ── Discounts: pass benefit first (an entitlement the holder already paid for), then
            // a coupon on what's left, mirroring the event-checkout ordering. The buyer's account
            // (id, or email resolved to one) is what carries a benefit — a walk-in gets none.
            var buyerUserId = req.BuyerUserId;
            if (buyerUserId is null && !string.IsNullOrWhiteSpace(req.BuyerEmail))
            {
                buyerUserId = (await _users.GetByEmail(TenantId, req.BuyerEmail.Trim()))?.Id;
            }
            // Per-pass benefit first, then the tenant-wide holder discount if no per-pass perk
            // applies. One resolver so this register, the online store, both rental paths and the
            // F&B till cannot drift on the precedence.
            var perk = await _perks.Resolve(buyerUserId, _tenantContext.Tenant, "retail", subtotal, DateTime.UtcNow);
            var benefitDiscount = perk.DiscountCents;

            // Staff-applied discount from the tenant list, taken after the pass benefit and before
            // the coupon. Ordering matters because each is computed on what's left: the entitlement
            // the customer already paid for comes off first, then what the counter chose to give,
            // then a code the customer brought. Stacking is permitted and simply compounds; the
            // cap below stops the total ever exceeding the sale.
            Services.Repositories.Data.DiscountData.DiscountPreset? staffDiscount = null;
            var staffDiscountCents = 0;
            Guid? discountAuthorizedBy = null;
            if (req.DiscountPresetId is Guid presetId)
            {
                staffDiscount = await _discounts.Get(presetId, TenantId);
                if (staffDiscount is null || !staffDiscount.IsActive)
                    return new ApiResponses().BadRequestResult("That discount isn't available.");
                // A discount scoped to food or the gate must not come off a set of grips just
                // because a cashier knows its id.
                if (!staffDiscount.AppliesTo(Services.Repositories.Data.DiscountData.DiscountSurfaces.ShopSale))
                    return new ApiResponses().BadRequestResult(
                        $"\"{staffDiscount.Name}\" doesn't apply to bike shop sales.");

                // Whether a PIN is needed is the tenant's per-discount choice, and it is decided
                // here rather than trusted from the client.
                if (staffDiscount.RequiresManager)
                {
                    if (UserId is not Guid staffUserId)
                        return new ApiResponses().BadRequestResult("Not signed in.");
                    var pin = await _managerPin.VerifyAsync(TenantId, staffUserId, req.ManagerPin);
                    if (!pin.Authorized)
                        return new ApiResponses().BadRequestResult(
                            pin.Error ?? $"A manager PIN is required to apply \"{staffDiscount.Name}\".");
                    discountAuthorizedBy = pin.AuthorizedUserId;
                }

                staffDiscountCents = staffDiscount.DiscountFor(Math.Max(0, subtotal - benefitDiscount));
            }

            Services.Repositories.Data.CouponData.CouponApplication? couponApp = null;
            if (!string.IsNullOrWhiteSpace(req.CouponCode))
            {
                var v = await _couponValidator.ValidateAsync(TenantId, req.CouponCode!,
                    scope: "shop", eventId: null,
                    subtotalCents: Math.Max(0, subtotal - benefitDiscount - staffDiscountCents), userId: buyerUserId);
                if (v.error is not null) return new ApiResponses().BadRequestResult(v.error);
                couponApp = v.application;
            }
            // Stacking is a tenant policy (Script0254) and defaults to OFF. When off, exactly one
            // discount applies and it is the largest, so the customer still gets the best deal
            // going without three of them compounding. Whichever loses is zeroed here rather than
            // earlier, because each was computed against the remaining base and the comparison
            // only makes sense once all three are known.
            var applied = Services.Discounts.DiscountStacking.Resolve(
                benefitDiscount, staffDiscountCents, couponApp?.DiscountCents ?? 0,
                _tenantContext.Tenant.AllowDiscountStacking);
            benefitDiscount = applied.BenefitCents;
            staffDiscountCents = applied.StaffCents;
            // Losers are cleared so nothing downstream records a discount that wasn't given: a
            // dropped coupon must not be marked redeemed, and a dropped staff discount must not be
            // snapshotted on the sale as though it had been applied.
            if (applied.CouponCents == 0) couponApp = null;
            if (staffDiscountCents == 0) { staffDiscount = null; discountAuthorizedBy = null; }

            var discountTotal = Math.Min(subtotal, applied.Total);

            // Spread the discount across lines by price share (largest remainder takes the last
            // cents) so per-line tax is computed on what was actually paid for that line.
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
            var taxTotalComputed = 0;
            foreach (var l in saleLines)
            {
                var net = Math.Max(0, l.UnitPriceCents * l.Quantity - l.DiscountCents);
                l.TaxCents = ComputeLineTax(net, l.TaxRateBps, pricesIncludeTax);
                taxTotalComputed += l.TaxCents;
            }
            taxTotal = taxTotalComputed;

            // The platform service charge, on the discounted goods and never on tax. Snapshotted
            // onto the sale so a later settings change cannot rewrite what a past sale owed.
            //
            // shop_buyer_paid_service_charge_bps decides only who FUNDS it. At the default 0 the
            // track absorbs it, `buyerFee` is 0, and the customer's total is exactly what it was
            // before this existed: no fee line appears on a walk-in buying a tube. The charge is
            // still owed, and the ledger books it against the track's proceeds.
            var (shopServiceCharge, buyerFee) = Services.Payments.ServiceChargeSplit.Compute(
                subtotal - discountTotal,
                _tenantContext.Tenant.ServiceChargeBps,
                _tenantContext.Tenant.ShopBuyerPaidServiceChargeBps);


            // Tax on the buyer's share of the fee, at the tenant's default category rate. Only
            // queried when there is actually a fee to tax, so the default configuration (fee 0)
            // pays nothing for this. See Services.Payments.ShopFeeTax.
            var feeTaxCents = 0;
            if (buyerFee > 0 && _tenantContext.Tenant.ShopTaxServiceChargeTaxable)
            {
                var defaultRate = (await _shop.ListTaxCategories(TenantId, activeOnly: true))
                    .FirstOrDefault(c => c.IsDefault)?.RateBps;
                feeTaxCents = Services.Payments.ShopFeeTax.Compute(
                    buyerFee, taxable: true, defaultRate, pricesIncludeTax);
                taxTotal += feeTaxCents;
            }

            // total = discounted goods + the buyer's share of the fee + tax on the net (including
            // the fee's own tax). No tip: a parts sale isn't tipped.
            var total = subtotal - discountTotal + buyerFee + taxTotal;

            // ── Gift card tender (after discounts, before store credit). The balance is debited
            // up front with an atomic conditional decrement; a failed payment hands it back via
            // the finalizer's RestoreDiscountsFor, and the aborts below restore it inline.
            Services.Repositories.Data.GiftCardData.GiftCardApplication? gcApp = null;
            var giftApplied = 0;
            if (!string.IsNullOrWhiteSpace(req.GiftCardCode) && total > 0)
            {
                var gcCheck = await _giftCardValidator.ResolveAsync(TenantId, req.GiftCardCode!, total);
                if (gcCheck.error is not null) return new ApiResponses().BadRequestResult(gcCheck.error);
                gcApp = gcCheck.application;
                if (!await _giftCards.ApplyToBalance(gcApp!.Card.Id, gcApp.AmountToApplyCents))
                    return new ApiResponses().BadRequestResult(
                        "That gift card's balance just changed. Re-apply it and try again.");
                giftApplied = gcApp.AmountToApplyCents;
            }
            async Task RestoreGiftCard()
            {
                if (gcApp is not null) await _giftCards.RestoreBalance(gcApp.Card.Id, giftApplied);
            }

            // ── Store credit tender: verify the account and cap at balance + what's left after
            // the gift card; the money path (cash or PI) collects only the remainder. The redeem
            // entry is written after the sale row exists; failures hand the credit back.
            Services.Repositories.Data.CreditData.TenantCreditAccount? creditAccount = null;
            var creditApplied = 0;
            if (req.CreditAccountId is not null && req.CreditCents > 0)
            {
                creditAccount = await _credit.GetAccount(req.CreditAccountId.Value, TenantId);
                if (creditAccount is null)
                {
                    await RestoreGiftCard();
                    return new ApiResponses().BadRequestResult("That store credit account no longer exists. Look it up again.");
                }
                creditApplied = Math.Min(Math.Min(req.CreditCents, creditAccount.BalanceCents), total - giftApplied);
            }
            var due = total - giftApplied - creditApplied;
            var isCard = req.PaymentMethod == "card" && due > 0;
            var tenant = _tenantContext.Tenant;

            // Card pre-checks before any row is created, so a rejected card sale leaves no orphan.
            if (isCard)
            {
                if (due < 50)
                {
                    await RestoreGiftCard();
                    return new ApiResponses().BadRequestResult("Less than 50 cents is due after the gift card and credit. Take cash for the remainder.");
                }
                if (tenant.StripeChargeMode == "direct" && string.IsNullOrEmpty(tenant.StripeConnectAccountId))
                {
                    await RestoreGiftCard();
                    return new ApiResponses().BadRequestResult("This track charges on its own Stripe account but hasn't connected one yet.");
                }
            }

            var sale = new ShopSale
            {
                TenantId = TenantId,
                BuyerUserId = buyerUserId,
                BuyerEmail = string.IsNullOrWhiteSpace(req.BuyerEmail) ? null : req.BuyerEmail.Trim(),
                BuyerName = string.IsNullOrWhiteSpace(req.BuyerName) ? null : req.BuyerName.Trim(),
                Status = "pending",
                SubtotalCents = subtotal,
                DiscountCents = discountTotal,
                // Snapshot which staff discount was used and who allowed it. discount_cents alone
                // can't answer "why is this $18" once a coupon and a pass benefit can also be in it,
                // so the label names every source that actually contributed after stacking.
                DiscountPresetId = staffDiscount?.Id,
                DiscountLabel = Services.Pricing.SeasonPassPerk.LabelFor(perk, benefitDiscount, staffDiscount?.Name, staffDiscountCents),
                DiscountAuthorizedByUserId = discountAuthorizedBy,
                TaxCents = taxTotal,
                TotalCents = total,
                ServiceChargeCents = shopServiceCharge,
                CreditAppliedCents = creditApplied,
                CreditAccountId = creditApplied > 0 ? creditAccount!.Id : null,
                GiftCardAppliedCents = giftApplied,
                GiftCardId = gcApp?.Card.Id,
                PricesIncludeTax = pricesIncludeTax,
                PaymentMethod = isCard ? "stripe" : "cash",
                SoldByUserId = UserId,
            };
            Guid saleId; Guid receipt;
            try
            {
                (saleId, receipt) = await _shop.CreateSale(sale, saleLines);
            }
            catch
            {
                // The gift balance was debited up front; a failed insert must hand it back.
                await RestoreGiftCard();
                throw;
            }

            // A staff-applied discount is money off with nothing the customer had to produce, so it
            // belongs in the same review surface as refunds and credit grants. Logged at the point
            // it was applied rather than when the sale settles: a discount taken on a card sale that
            // then fails is still a thing someone did. The sale row carries the same snapshot; what
            // this adds is the actor and the address it came from.
            if (staffDiscount is not null && staffDiscountCents > 0)
            {
                await _audit.Log(
                    "shop.discount_applied",
                    $"Applied \"{staffDiscount.Name}\" to a shop sale, taking off ${staffDiscountCents / 100m:0.00}",
                    targetKind: "shop_sale",
                    targetId: saleId,
                    tenantId: TenantId,
                    metadata: new
                    {
                        discountName = staffDiscount.Name,
                        discountPresetId = staffDiscount.Id,
                        discountCents = staffDiscountCents,
                        subtotalCents = subtotal,
                        requiredManager = staffDiscount.RequiresManager,
                        // Null unless the discount was PIN-gated; names who allowed it.
                        authorizedByUserId = discountAuthorizedBy,
                    });
            }

            if (creditApplied > 0 &&
                !await _credit.TryAdjust(creditAccount!.Id, TenantId, -creditApplied, "redeem",
                    "shop_sale", saleId, null, UserId))
            {
                // Balance moved between lookup and ring-up (another register beat us to it).
                await _shop.MarkSaleFailed(saleId);
                await RestoreGiftCard();
                return new ApiResponses().BadRequestResult(
                    "The store credit balance changed while ringing up. Look the customer up again.");
            }

            if (gcApp is not null)
            {
                // Recorded so a failed payment restores the balance (finalizer RestoreDiscountsFor)
                // and reporting can trace the redemption.
                await _giftCards.RecordRedemption(new Services.Repositories.Data.GiftCardData.GiftCardRedemption
                {
                    GiftCardId = gcApp.Card.Id,
                    TenantId = TenantId,
                    UserId = buyerUserId,
                    SourceKind = "shop_sale",
                    SourceId = saleId,
                    AmountCents = giftApplied,
                });
            }

            if (couponApp is not null)
            {
                // Recorded up front like every other flow; a failed card sale hands the use back
                // via the finalizer's RestoreDiscountsFor.
                await _coupons.RecordRedemption(new Services.Repositories.Data.CouponData.CouponRedemption
                {
                    CouponId = couponApp.Coupon.Id,
                    TenantId = TenantId,
                    UserId = buyerUserId,
                    SourceKind = "shop_sale",
                    SourceId = saleId,
                    DiscountCents = couponApp.DiscountCents,
                });
            }

            // ── Cash / fully-covered: paid at the counter now ─────────────────────
            if (!isCard)
            {
                if (await _shop.TryMarkSalePaid(saleId, TenantId))
                {
                    var orderNumber = await _shop.NextOrderNumber(TenantId);
                    await _shop.SetSaleOrderNumber(saleId, orderNumber);
                    try { await _shop.DepleteForSale(saleId, TenantId, UserId); }
                    catch { /* inventory depletion is best-effort; the sale is paid regardless */ }
                    if (due + giftApplied > 0) await WriteCashLedger(saleId, due, giftApplied, shopServiceCharge);
                    await NotifyLowStock();
                    return new ApiResponses().OkResult(new
                    {
                        saleId, receiptToken = receipt, status = "paid", orderNumber, totalCents = total,
                        discountCents = discountTotal, creditAppliedCents = creditApplied,
                        giftCardAppliedCents = giftApplied, dueCents = due,
                        subtotalCents = subtotal, taxCents = taxTotal,
                    });
                }
                return new ApiResponses().OkResult(new { saleId, receiptToken = receipt, status = "paid",
                    totalCents = total, creditAppliedCents = creditApplied, giftCardAppliedCents = giftApplied, dueCents = due });
            }

            // ── Card: create a PaymentIntent (on-screen Payment Element, or card-present on the
            // reader); the finalizer completes it on the webhook either way. ─────────────────
            var metadata = new Dictionary<string, string>
            {
                ["tenant_id"] = TenantId.ToString(),
                ["sale_kind"] = "shop_sale",
                ["shop_sale_id"] = saleId.ToString(),
            };
            async Task AbortCardStart()
            {
                await _shop.MarkSaleFailed(saleId);
                await _credit.ReverseRedeem(TenantId, "shop_sale", saleId, "payment could not start");
                var removed = await _giftCards.DeleteRedemptionsBySource("shop_sale", new[] { saleId });
                foreach (var byCard in removed.GroupBy(r => r.GiftCardId))
                    await _giftCards.RestoreBalance(byCard.Key, byCard.Sum(r => r.AmountCents));
            }
            PaymentIntentCreated intent;
            ChargePlan plan;
            try
            {
                // In direct mode the money lands on the track's own Stripe account, so the
                // application fee is the ONLY way RidePass collects its charge. Route the figure
                // snapshotted on the sale, not a zero: a zero here would have the ledger book a
                // cut that never actually moved. It routes whoever funds it, since absorbing the
                // charge means it comes out of the track's proceeds by design. ChargeRouter clamps
                // it to the amount being charged, which matters when gift cards or store credit
                // have shrunk `due` below the fee.
                plan = _chargeRouter.Plan(tenant, serviceFeeCents: shopServiceCharge, chargeAmountCents: due);
                if (req.CardPresent)
                {
                    var locationId = await EnsureTerminalLocation(plan.ConnectedAccountId, ct);
                    if (locationId is null)
                    {
                        await AbortCardStart();
                        return new ApiResponses().BadRequestResult(
                            "Card-present payments need the track's address filled in (Settings, General).");
                    }
                    intent = await _payments.CreateCardPresentPaymentIntentAsync(due, "usd", locationId, metadata,
                        sale.BuyerEmail, connectedAccountId: plan.ConnectedAccountId,
                        applicationFeeCents: plan.ApplicationFeeCents, ct: ct);
                }
                else
                {
                    intent = await _payments.CreatePaymentIntentAsync(due, "usd", metadata, sale.BuyerEmail,
                        connectedAccountId: plan.ConnectedAccountId, applicationFeeCents: plan.ApplicationFeeCents, ct: ct);
                }
            }
            catch (InvalidOperationException ex)
            {
                await AbortCardStart();
                return new ApiResponses().BadRequestResult(ex.Message);
            }

            await _shop.SetSalePaymentIntent(saleId, intent.IntentId);
            if (plan.IsDirect) await _shop.MarkSaleDirectCharge(saleId, TenantId, plan.ConnectedAccountId!);

            return new ApiResponses().OkResult(new
            {
                saleId, receiptToken = receipt, status = "pending",
                clientSecret = intent.ClientSecret, paymentIntentId = intent.IntentId,
                cardPresent = req.CardPresent, totalCents = total,
                creditAppliedCents = creditApplied, giftCardAppliedCents = giftApplied, dueCents = due,
                subtotalCents = subtotal, taxCents = taxTotal, discountCents = discountTotal,
            });
        }

        // Same lazy Location provisioning as the F&B and gate registers (the token + Location
        // must live on the connected account in direct mode).
        private async Task<string?> EnsureTerminalLocation(string? connectedAccountId, CancellationToken ct)
        {
            var tenant = _tenantContext.Tenant;
            var direct = !string.IsNullOrEmpty(connectedAccountId);
            var existing = direct ? tenant.StripeConnectedTerminalLocationId : tenant.StripeTerminalLocationId;
            if (!string.IsNullOrWhiteSpace(existing)) return existing;
            if (string.IsNullOrWhiteSpace(tenant.AddressLine) || string.IsNullOrWhiteSpace(tenant.City)
                || string.IsNullOrWhiteSpace(tenant.Country) || string.IsNullOrWhiteSpace(tenant.PostalCode))
                return null;
            string locationId;
            try
            {
                locationId = await _payments.CreateTerminalLocationAsync(
                    tenant.DisplayName,
                    new TerminalLocationAddress(
                        Line1: tenant.AddressLine, City: tenant.City,
                        Country: tenant.Country, PostalCode: tenant.PostalCode, State: tenant.Region),
                    connectedAccountId, ct);
            }
            catch (InvalidOperationException) { return null; }
            if (direct) await _tenants.SetStripeConnectedTerminalLocationId(TenantId, locationId);
            else await _tenants.SetStripeTerminalLocationId(TenantId, locationId);
            return locationId;
        }

        // The reader SDK's connection token, reachable by shop cashiers (the F&B/gate token
        // endpoints are SalesCounter-gated).
        [Authorize(Policy = TenantPermissions.Policy.ShopCounter)]
        [HttpPost("Terminal/ConnectionToken")]
        public async Task<IActionResult> CreateTerminalConnectionToken(CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var tenant = _tenantContext.Tenant;
            if (tenant.StripeChargeMode == "direct" && string.IsNullOrEmpty(tenant.StripeConnectAccountId))
                return new ApiResponses().BadRequestResult(
                    "This track charges on its own Stripe account but hasn't connected one yet.");
            var connectedAccountId = tenant.StripeChargeMode == "direct" ? tenant.StripeConnectAccountId : null;
            var locationId = await EnsureTerminalLocation(connectedAccountId, ct);
            if (locationId is null)
                return new ApiResponses().BadRequestResult(
                    "Card-present payments need the track's address filled in (Settings, General).");
            try
            {
                var secret = await _payments.CreateTerminalConnectionTokenAsync(locationId, connectedAccountId, ct: ct);
                return new ApiResponses().OkResult(new { secret, locationId });
            }
            catch (InvalidOperationException ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        [Authorize(Policy = TenantPermissions.Policy.SalesRefund)]
        [HttpPost("Sale/{id:guid}/Refund")]
        public async Task<IActionResult> Refund(Guid id, [FromBody] RefundShopSaleRequest req, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var sale = await _shop.GetSale(id, TenantId);
            if (sale is null) return new ApiResponses().NotFoundResult("Sale not found.");
            if (sale.Status != "paid")
                return new ApiResponses().BadRequestResult(
                    sale.Status == "refunded" ? "This sale has already been refunded." : "Only a paid sale can be refunded.");

            // What money actually moved on this sale's PI (deposit, store credit, and gift card
            // were separate value).
            var moneyPortion = sale.TotalCents - sale.DepositAppliedCents - sale.CreditAppliedCents
                - sale.GiftCardAppliedCents;
            var toCredit = string.Equals(req.Destination, "credit", StringComparison.OrdinalIgnoreCase);

            Services.Repositories.Data.CreditData.TenantCreditAccount? creditAccount = null;
            if (toCredit && moneyPortion > 0)
            {
                creditAccount = await _credit.GetOrCreateAccount(TenantId,
                    sale.BuyerUserId, sale.BuyerEmail, null, sale.BuyerName);
                if (creditAccount is null)
                    return new ApiResponses().BadRequestResult(
                        "Refund-to-credit needs a customer email or account on the sale. Refund to the original payment instead.");
            }

            // Card + original destination: reverse on the account it was charged on (direct sales
            // also return our app fee). Cash: the cashier hands the money back from the drawer.
            // The idempotency key makes a double-submit a no-op on Stripe's side.
            if (!toCredit && moneyPortion > 0 &&
                sale.PaymentMethod is "stripe" or "stripe_direct" && !string.IsNullOrEmpty(sale.StripePaymentIntentId))
            {
                var isDirect = sale.PaymentMethod == "stripe_direct";
                try
                {
                    // The PI only charged the money portion; deposit, credit, and gift card go
                    // back on their own tracks (deposit endpoint, credit + gift restores below).
                    await _payments.RefundAsync(sale.StripePaymentIntentId!, moneyPortion,
                        idempotencyKey: $"shop_refund_{sale.Id}",
                        connectedAccountId: isDirect ? sale.StripeConnectedAccountId : null,
                        refundApplicationFee: isDirect, ct: ct);
                }
                catch (Exception ex)
                {
                    // Money didn't go back, so don't mark the sale refunded.
                    return new ApiResponses().BadRequestResult($"Could not refund the card: {ex.Message}");
                }
            }

            // Gate: only the call that flips paid -> refunded runs restock + the ledger reversal.
            if (await _shop.MarkSaleRefunded(sale.Id, TenantId, string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim()) == 0)
                return new ApiResponses().BadRequestResult("This sale has already been refunded.");

            if (req.Restock)
            {
                try { await _shop.RestockForSale(sale.Id, TenantId, UserId); }
                catch { /* restock is best-effort; the refund itself already went through */ }
            }

            // Credit the sale was paid with goes back to its account either way, and gift-card
            // value returns to the card (never converted to cash or store credit).
            if (sale.CreditAppliedCents > 0)
                await _credit.ReverseRedeem(TenantId, "shop_sale", sale.Id, "sale refunded");
            if (sale.GiftCardAppliedCents > 0 && sale.GiftCardId is not null)
            {
                var removed = await _giftCards.DeleteRedemptionsBySource("shop_sale", new[] { sale.Id });
                foreach (var byCard in removed.GroupBy(r => r.GiftCardId))
                    await _giftCards.RestoreBalance(byCard.Key, byCard.Sum(r => r.AmountCents));
            }

            if (toCredit && moneyPortion > 0)
            {
                // The tenant keeps the cash and owes value instead, so there is deliberately NO
                // ledger mirror here: the original sale entry stands (money stayed in), and the
                // liability now lives on the credit account.
                await _credit.TryAdjust(creditAccount!.Id, TenantId, moneyPortion, "refund_to_credit",
                    "shop_sale", sale.Id, string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim(), UserId);
            }
            else
            {
                await WriteRefundLedger(sale);
            }

            // Third of the three refund paths. This one carries a vector the others don't: the
            // refund can be redirected to a store-credit account instead of back to the original
            // payment, so the destination is recorded explicitly. A cash sale refunded to credit
            // on an account the staff member controls is money leaving the till with the value
            // landing somewhere they can spend it.
            await _audit.Log(
                "shop.refund",
                $"Refunded a ${sale.TotalCents / 100m:0.00} shop sale ({sale.PaymentMethod}) "
                    + $"to {(toCredit ? "store credit" : "the original payment")}",
                targetKind: "shop_sale",
                targetId: sale.Id,
                tenantId: TenantId,
                metadata: new
                {
                    totalCents = sale.TotalCents,
                    moneyPortionCents = moneyPortion,
                    paymentMethod = sale.PaymentMethod,
                    stripePaymentIntentId = sale.StripePaymentIntentId,
                    destination = toCredit ? "credit" : "original",
                    creditedCents = toCredit ? moneyPortion : 0,
                    creditAccountId = toCredit ? creditAccount?.Id : null,
                    restocked = req.Restock,
                    note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim(),
                });

            return new ApiResponses().OkResult(new
            {
                status = "refunded", restocked = req.Restock,
                destination = toCredit ? "credit" : "original",
                creditedCents = toCredit ? moneyPortion : 0,
            });
        }

        // Negative mirror of the original sale entry, so payouts and reports net to zero on a refund.
        private async Task WriteRefundLedger(Services.Repositories.Data.BikeShopData.ShopSale sale)
        {
            var entry = await _ledger.GetSaleEntryForSource(TenantId, "shop_sale", sale.Id);
            if (entry is null) return;
            try
            {
                await _ledger.Insert(new TenantLedgerEntry
                {
                    TenantId = TenantId,
                    EntryKind = "refund",
                    SourceKind = "shop_sale",
                    SourceId = sale.Id,
                    OccurredAtUtc = DateTime.UtcNow,
                    GrossCents = -entry.GrossCents,
                    StripeFeeCents = -entry.StripeFeeCents,
                    RidepassCutCents = -entry.RidepassCutCents,
                    NetToTenantCents = -entry.NetToTenantCents,
                    StripePaymentIntentId = entry.StripePaymentIntentId,
                    PaymentMethod = sale.PaymentMethod,
                    SoldByUserId = UserId,
                    Memo = "Bike shop refund",
                });
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505") { /* idempotent */ }
        }

        // ── Sales history + receipts ──────────────────────────────────────────────
        // A customer's whole shop footprint: sales, rentals, work orders, credit balance. Serves
        // the admin CustomerDetail page (CustomersView) AND the bench's "what did we do for this
        // bike last time" lookup (ShopCounter); policies can only AND, so the OR is by hand.
        [HttpGet("CustomerHistory")]
        public async Task<IActionResult> CustomerHistory(
            [FromQuery] Guid? userId, [FromQuery] string? query, [FromQuery] int limit = 25)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var roles = User.FindAll("role").Select(c => c.Value).ToList();
            var perms = TenantPermissions.ForRoles(roles);
            if (!roles.Contains("super_admin")
                && !perms.Contains(TenantPermissions.CustomersView)
                && !perms.Contains(TenantPermissions.ShopCounter))
                return Forbid();
            if (userId is null && string.IsNullOrWhiteSpace(query))
                return new ApiResponses().BadRequestResult("Give a customer account, email, or phone to look up.");

            // A free-text query could be either identity; try it as both.
            var q = query?.Trim();
            var (sales, rentals, workOrders) = await _shop.GetCustomerHistory(
                TenantId, userId, q, q, Math.Clamp(limit, 1, 100));

            var creditBalance = 0;
            var creditAccount = userId is not null
                ? await _credit.GetAccountForUser(TenantId, userId.Value)
                : (q is null ? null : await _credit.LookupAccount(TenantId, q));
            if (creditAccount is not null) creditBalance = creditAccount.BalanceCents;

            return new ApiResponses().OkResult(new
            {
                sales = sales.Select(s => new
                {
                    s.Id, s.CreatedAt, s.Status, s.TotalCents, s.OrderNumber, s.PaymentMethod,
                    isRepair = s.WorkOrderId is not null,
                }),
                rentals = rentals.Select(r => new
                {
                    r.Id, r.StartsAt, r.EndsAt, r.Status, r.TotalCents, r.DepositCents,
                }),
                workOrders = workOrders.Select(w => new
                {
                    w.Id, w.CreatedAt, w.Status, w.CustomerBikeDesc, w.PromisedAt,
                }),
                creditBalanceCents = creditBalance,
            });
        }

        [Authorize(Policy = TenantPermissions.Policy.ShopCounter)]
        [HttpGet("Sales")]
        public async Task<IActionResult> ListSales([FromQuery] ShopSalesRequest request)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (request.From.HasValue && request.To.HasValue && request.To < request.From)
                return new ApiResponses().BadRequestResult("The end date is before the start date.");
            return new ApiResponses().OkResult(await _shop.SearchSales(TenantId, request.ToQuery()));
        }

        // Hand over an online order at the counter.
        [Authorize(Policy = TenantPermissions.Policy.ShopCounter)]
        [HttpPost("Sale/{id:guid}/PickedUp")]
        public async Task<IActionResult> MarkPickedUp(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!await _shop.MarkSalePickedUp(id, TenantId))
                return new ApiResponses().BadRequestResult(
                    "This isn't a paid online order awaiting pickup (it may already be collected).");
            return new ApiResponses().OkResult();
        }

        [Authorize(Policy = TenantPermissions.Policy.ShopCounter)]
        [HttpPost("Sale/{id:guid}/Receipt")]
        public async Task<IActionResult> SendReceipt(Guid id, [FromBody] ShopReceiptRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var sale = await _shop.GetSale(id, TenantId);
            if (sale is null) return new ApiResponses().NotFoundResult("Sale not found.");
            var dest = req.Destination.Trim();
            if (dest.Length == 0) return new ApiResponses().BadRequestResult("Enter where to send the receipt.");
            var tenant = _tenantContext.Tenant;

            if (string.Equals(req.Channel, "sms", StringComparison.OrdinalIgnoreCase))
            {
                if (!_sms.IsConfiguredFor(tenant))
                    return new ApiResponses().BadRequestResult("Text receipts aren't set up for this track.");
                if (!await _sms.Send(tenant, dest, BuildReceiptText(tenant.DisplayName, sale)))
                    return new ApiResponses().BadRequestResult("Could not send the text receipt.");
            }
            else
            {
                if (!_emailer.IsConfigured)
                    return new ApiResponses().BadRequestResult("Email receipts aren't set up.");
                var subject = $"{tenant.DisplayName} receipt{(sale.OrderNumber is not null ? $" — Sale #{sale.OrderNumber}" : "")}";
                if (!await _emailer.Send(dest, subject, BuildReceiptHtml(tenant.DisplayName, sale), null,
                        Services.Email.TenantEmailIdentity.For(tenant)))
                    return new ApiResponses().BadRequestResult("Could not send the email receipt.");
            }
            return new ApiResponses().OkResult();
        }

        private static string ReceiptMoney(int cents) => "$" + (cents / 100m).ToString("0.00");

        private static string BuildReceiptText(string header, Services.Repositories.Data.BikeShopData.ShopSaleWithLines sale)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(header);
            if (sale.OrderNumber is not null) sb.AppendLine($"Sale #{sale.OrderNumber}");
            foreach (var l in sale.Lines)
            {
                var name = !string.IsNullOrWhiteSpace(l.VariantLabel) ? $"{l.NameSnapshot} ({l.VariantLabel})" : l.NameSnapshot;
                sb.AppendLine($"{l.Quantity}x {name}  {ReceiptMoney(l.UnitPriceCents * l.Quantity)}");
            }
            sb.AppendLine($"Subtotal {ReceiptMoney(sale.SubtotalCents)}");
            if (sale.DiscountCents > 0) sb.AppendLine($"Discount -{ReceiptMoney(sale.DiscountCents)}");
            if (sale.TaxCents > 0) sb.AppendLine($"Tax {ReceiptMoney(sale.TaxCents)}");
            sb.AppendLine($"Total {ReceiptMoney(sale.TotalCents)}");
            return sb.ToString();
        }

        private static string BuildReceiptHtml(string header, Services.Repositories.Data.BikeShopData.ShopSaleWithLines sale)
        {
            static string Enc(string s) => System.Net.WebUtility.HtmlEncode(s);
            var sb = new System.Text.StringBuilder();
            sb.Append("<div style=\"font-family:Arial,Helvetica,sans-serif;max-width:380px\">");
            sb.Append($"<h2 style=\"margin:0 0 4px\">{Enc(header)}</h2>");
            if (sale.OrderNumber is not null)
                sb.Append($"<p style=\"font-size:18px;font-weight:bold;margin:0 0 12px\">Sale #{sale.OrderNumber}</p>");
            sb.Append("<table style=\"width:100%;border-collapse:collapse;font-size:14px\">");
            foreach (var l in sale.Lines)
            {
                var name = !string.IsNullOrWhiteSpace(l.VariantLabel) ? $"{l.NameSnapshot} ({l.VariantLabel})" : l.NameSnapshot;
                sb.Append($"<tr><td style=\"padding:2px 0\">{l.Quantity}&times; {Enc(name)}</td><td style=\"text-align:right\">{ReceiptMoney(l.UnitPriceCents * l.Quantity)}</td></tr>");
            }
            sb.Append("</table><hr style=\"border:none;border-top:1px solid #ddd;margin:8px 0\" />");
            sb.Append("<table style=\"width:100%;border-collapse:collapse;font-size:14px\">");
            sb.Append($"<tr><td>Subtotal</td><td style=\"text-align:right\">{ReceiptMoney(sale.SubtotalCents)}</td></tr>");
            if (sale.DiscountCents > 0)
                sb.Append($"<tr><td>Discount</td><td style=\"text-align:right\">-{ReceiptMoney(sale.DiscountCents)}</td></tr>");
            if (sale.TaxCents > 0)
                sb.Append($"<tr><td>Tax</td><td style=\"text-align:right\">{ReceiptMoney(sale.TaxCents)}</td></tr>");
            sb.Append($"<tr><td style=\"font-weight:bold\">Total</td><td style=\"text-align:right;font-weight:bold\">{ReceiptMoney(sale.TotalCents)}</td></tr>");
            sb.Append("</table></div>");
            return sb.ToString();
        }

        // Alert managers about anything this sale just pushed to/below its threshold. Best-effort,
        // and a whole-tenant sweep, so consumption from other paths (work orders) gets caught by
        // the next sale too.
        private async Task NotifyLowStock()
        {
            try
            {
                var low = await _shop.MarkAndGetNewlyLowShopStock(TenantId);
                if (low.Count == 0) return;
                var names = string.Join(", ", low.Select(i =>
                    $"{i.ProductName}{(i.VariantLabel is null ? "" : $" ({i.VariantLabel})")} — {i.Available} left"));
                var title = low.Count == 1 ? "1 shop item low on stock" : $"{low.Count} shop items low on stock";
                await _notifications.EmitToTenantRoles(TenantId, new[] { "tenant_manager", "tenant_admin" },
                    Services.Notifications.NotificationKinds.LowStock, title, $"Running low: {names}.", "/Admin/BikeShop");
            }
            catch { /* alerting is best-effort */ }
        }

        // Cash convention (mirrors concessions): the tenant holds the drawer cash, so they owe us our
        // cut. Gross recorded for reporting; net to tenant = -cut.
        // Cash-path sale entry. Gross recognizes the cash collected PLUS any gift-card-funded
        // portion (gift purchases book nothing; revenue lands at redemption). Net: the tenant
        // holds the cash themselves, so they owe the cut; the gift funds sit wherever the card
        // was bought (platform account in platform mode: owed to the tenant; tenant's own
        // account in direct mode: nothing to move).
        private async Task WriteCashLedger(Guid saleId, int cashCents, int giftCents = 0, int serviceChargeCents = 0)
        {
            try
            {
                var gross = cashCents + giftCents;
                var isDirect = _tenantContext.Tenant.StripeChargeMode == "direct";
                // The charge snapshotted on the sale, not a zero. FeeCalculator does not compute
                // one, it only applies the tenant's monthly cap to the figure it is handed, so
                // passing 0 here is what made every bike shop sale free to the platform while the
                // memo below claimed the tenant owed something.
                var calc = await _feeCalculator.Calculate(TenantId, gross, 0, serviceChargeCents, DateTime.UtcNow);
                await _ledger.Insert(new TenantLedgerEntry
                {
                    TenantId = TenantId,
                    EntryKind = "sale",
                    SourceKind = "shop_sale",
                    SourceId = saleId,
                    OccurredAtUtc = DateTime.UtcNow,
                    GrossCents = gross,
                    StripeFeeCents = 0,
                    RidepassCutCents = calc.RidepassCutCents,
                    NetToTenantCents = (isDirect ? 0 : giftCents) - calc.RidepassCutCents,
                    PaymentMethod = "cash",
                    SoldByUserId = UserId,
                    Memo = giftCents > 0
                        ? "Bike shop sale, cash + gift card"
                        : "Bike shop cash sale, tenant owes service charge",
                });
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505") { /* idempotent */ }
        }

        private static string? VariantLabel(ShopVariantSaleInfo v)
        {
            var parts = new[] { v.Size, v.Color, v.Gender }.Where(s => !string.IsNullOrWhiteSpace(s));
            var label = string.Join(" / ", parts);
            return string.IsNullOrWhiteSpace(label) ? null : label;
        }

        private static int ComputeLineTax(int baseCents, int rateBps, bool pricesIncludeTax)
        {
            if (rateBps <= 0 || baseCents <= 0) return 0;
            if (pricesIncludeTax)
                return baseCents - (int)Math.Round(baseCents * 10000.0 / (10000.0 + rateBps), MidpointRounding.AwayFromZero);
            return (int)Math.Round(baseCents * rateBps / 10000.0, MidpointRounding.AwayFromZero);
        }
    }
}
