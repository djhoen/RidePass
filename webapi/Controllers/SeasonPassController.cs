using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Coupons;
using Services.Helpers;
using Services.Payments;
using Services.Repositories.Data.CouponData;
using Services.Repositories.Data.GiftCardData;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.SeasonPass;
using webapi.Helpers;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SeasonPassController : ControllerBase
    {
        private readonly ISeasonPassRepository _passes;
        private readonly IEventRepository _events;
        private readonly ITenantEventTypeRepository _eventTypes;
        private readonly IUserRepository _users;
        private readonly IPaymentProvider _payments;
        private readonly IChargeRouter _chargeRouter;
        private readonly ICouponRepository _coupons;
        private readonly ICouponValidator _couponValidator;
        private readonly IGiftCardRepository _giftCards;
        private readonly Services.GiftCards.IGiftCardValidator _giftCardValidator;
        private readonly IMembershipRepository _memberships;
        private readonly IWaiverRepository _waivers;
        private readonly Services.Waivers.IWaiverCheckInGate _waiverGate;
        private readonly ITenantLedgerRepository _ledger;
        private readonly Services.Payments.IFeeCalculator _feeCalculator;
        private readonly ITenantContext _tenantContext;

        public SeasonPassController(
            ISeasonPassRepository passes,
            IEventRepository events,
            ITenantEventTypeRepository eventTypes,
            IUserRepository users,
            IPaymentProvider payments,
            IChargeRouter chargeRouter,
            ICouponRepository coupons,
            ICouponValidator couponValidator,
            IGiftCardRepository giftCards,
            Services.GiftCards.IGiftCardValidator giftCardValidator,
            IMembershipRepository memberships,
            IWaiverRepository waivers,
            Services.Waivers.IWaiverCheckInGate waiverGate,
            ITenantLedgerRepository ledger,
            Services.Payments.IFeeCalculator feeCalculator,
            ITenantContext tenantContext)
        {
            _passes = passes;
            _events = events;
            _eventTypes = eventTypes;
            _users = users;
            _payments = payments;
            _chargeRouter = chargeRouter;
            _coupons = coupons;
            _couponValidator = couponValidator;
            _giftCards = giftCards;
            _giftCardValidator = giftCardValidator;
            _memberships = memberships;
            _waivers = waivers;
            _waiverGate = waiverGate;
            _ledger = ledger;
            _feeCalculator = feeCalculator;
            _tenantContext = tenantContext;
        }

        // ── Products: public list ─────────────────────────────────────────────────
        [HttpGet("Products")]
        public async Task<IActionResult> ListActive()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var products = await _passes.ListProductsForTenant(_tenantContext.TenantId, activeOnly: true);
            return new ApiResponses().OkResult(await ToResponses(products));
        }

        // ── Products: tenant-admin CRUD ───────────────────────────────────────────
        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpGet("Products/Admin")]
        public async Task<IActionResult> ListForAdmin()
        {
            var products = await _passes.ListProductsForTenant(_tenantContext.TenantId, activeOnly: false);
            return new ApiResponses().OkResult(await ToResponses(products));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost("Products")]
        public async Task<IActionResult> CreateProduct([FromBody] UpsertSeasonPassProductRequest request)
        {
            if (request.ValidToDate < request.ValidFromDate)
            {
                return new ApiResponses().BadRequestResult("Valid-to date must be on or after valid-from date.");
            }
            if (request.Kind == "credits" && (request.TotalCredits is null || request.TotalCredits <= 0))
            {
                return new ApiResponses().BadRequestResult("Credit-based passes need total_credits > 0.");
            }
            if (request.Kind == "days_of_week" && (request.ValidDaysOfWeek is null || request.ValidDaysOfWeek.Length == 0))
            {
                return new ApiResponses().BadRequestResult("Day-of-week passes need at least one valid day.");
            }

            var product = new SeasonPassProduct
            {
                TenantId = _tenantContext.TenantId,
                Name = request.Name.Trim(),
                Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
                PriceCents = request.PriceCents,
                ValidFromDate = request.ValidFromDate.Date,
                ValidToDate = request.ValidToDate.Date,
                Kind = request.Kind,
                ValidDaysOfWeek = request.Kind == "days_of_week" ? request.ValidDaysOfWeek : null,
                TotalCredits = request.Kind == "credits" ? request.TotalCredits : null,
                RequiresWaiver = request.RequiresWaiver,
                RiderPaidServiceChargeBps = request.RiderPaidServiceChargeBps,
                IsActive = request.IsActive,
                SortOrder = request.SortOrder,
            };
            product.Id = await _passes.CreateProduct(product);
            var createError = await SaveBenefits(product.Id, request);
            if (createError is not null) return new ApiResponses().BadRequestResult(createError);
            return new ApiResponses().OkResult(await ToResponse(product));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPut("Products/{id:guid}")]
        public async Task<IActionResult> UpdateProduct(Guid id, [FromBody] UpsertSeasonPassProductRequest request)
        {
            var existing = await _passes.GetProduct(id, _tenantContext.TenantId);
            if (existing is null) return new ApiResponses().NotFoundResult("Product not found.");
            if (request.ValidToDate < request.ValidFromDate)
            {
                return new ApiResponses().BadRequestResult("Valid-to date must be on or after valid-from date.");
            }
            existing.Name = request.Name.Trim();
            existing.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
            existing.PriceCents = request.PriceCents;
            existing.ValidFromDate = request.ValidFromDate.Date;
            existing.ValidToDate = request.ValidToDate.Date;
            existing.Kind = request.Kind;
            existing.ValidDaysOfWeek = request.Kind == "days_of_week" ? request.ValidDaysOfWeek : null;
            existing.TotalCredits = request.Kind == "credits" ? request.TotalCredits : null;
            existing.RequiresWaiver = request.RequiresWaiver;
            existing.RiderPaidServiceChargeBps = request.RiderPaidServiceChargeBps;
            existing.IsActive = request.IsActive;
            existing.SortOrder = request.SortOrder;
            await _passes.UpdateProduct(existing);
            var updateError = await SaveBenefits(existing.Id, request);
            if (updateError is not null) return new ApiResponses().BadRequestResult(updateError);
            return new ApiResponses().OkResult(await ToResponse(existing));
        }

        /// <summary>
        /// Persists a product's benefits, validating them first. Returns an error message, or null
        /// on success.
        /// </summary>
        /// <remarks>
        /// Dual-writes the legacy season_pass_event_type_perk table from the event benefits, because
        /// the deployed app still reads it (expand-then-contract — Script0178 leaves it in place).
        /// Once nothing reads it, the perk write and the table both go.
        ///
        /// Benefits win over the legacy Perks field when a client sends both: an older client sends
        /// only Perks, so we fall back to converting those instead of wiping a product's benefits.
        /// </remarks>
        private async Task<string?> SaveBenefits(Guid productId, UpsertSeasonPassProductRequest request)
        {
            var benefits = request.Benefits.Count > 0
                ? request.Benefits
                : request.Perks.Select(p => new SeasonPassBenefitInput
                {
                    BenefitType = "event",
                    ScopeId = p.EventTypeId,
                    DiscountKind = "percent",
                    DiscountValue = p.DiscountPercent * 100,
                }).ToList();

            var eventTypeIds = (await _eventTypes.GetAllForTenant(_tenantContext.TenantId))
                .Select(t => t.Id).ToHashSet();

            foreach (var b in benefits)
            {
                if (b.DiscountKind == "percent" && b.DiscountValue > 10_000)
                {
                    return "A percentage benefit can't be more than 100%.";
                }
                // A benefit worth nothing is almost always a half-filled row, and it would render
                // as "0% off" on the landing page.
                if (b.DiscountValue <= 0 && b.BenefitType != "buddy_pass")
                {
                    return "Each benefit needs a discount greater than zero.";
                }
                if (b.BenefitType == "event" && b.ScopeId.HasValue && !eventTypeIds.Contains(b.ScopeId.Value))
                {
                    // Tenant-scoped check: without it a spoofed id would attach a benefit to
                    // another tenant's event type, and checkout would price against it.
                    return "That event type doesn't exist at this track.";
                }
                // Non-event surfaces are whole-surface benefits; a scope id would silently never match.
                if (b.BenefitType is "concession" or "rental" or "retail" && b.ScopeId.HasValue)
                {
                    return "F&B, rental, and bike shop benefits apply to the whole surface — no scope.";
                }
                if (b.BenefitType == "buddy_pass" && (b.Quantity is null || b.Quantity < 1))
                {
                    return "Buddy passes need a quantity — how many the pass includes per season.";
                }
            }

            // Duplicates would violate the unique index and, worse, double-apply at checkout if it
            // ever slipped. Report it as a user error rather than a 500 from the constraint.
            var duplicate = benefits
                .GroupBy(b => (b.BenefitType, b.ScopeId))
                .Any(g => g.Count() > 1);
            if (duplicate) return "Each benefit can only be listed once per event type.";

            await _passes.ReplaceBenefits(productId, _tenantContext.TenantId, benefits.Select(b => new SeasonPassBenefit
            {
                BenefitType = b.BenefitType,
                ScopeId = b.ScopeId,
                DiscountKind = b.DiscountKind,
                DiscountValue = b.DiscountValue,
                Quantity = b.Quantity,
            }));

            await _passes.ReplacePerks(productId, benefits
                .Where(b => b.BenefitType == "event" && b.ScopeId.HasValue && b.DiscountKind == "percent")
                .Select(b => new SeasonPassEventTypePerk
                {
                    EventTypeId = b.ScopeId!.Value,
                    DiscountPercent = b.DiscountValue / 100,
                }));
            return null;
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost("Products/Reorder")]
        public async Task<IActionResult> ReorderProducts([FromBody] ReorderSeasonPassProductsRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (req.Items.Count == 0) return new ApiResponses().OkResult();
            var ids = req.Items.Select(i => i.Id).ToList();
            var orders = req.Items.Select(i => i.SortOrder).ToList();
            await _passes.UpdateProductSortOrders(_tenantContext.TenantId, ids, orders);
            return new ApiResponses().OkResult();
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpDelete("Products/{id:guid}")]
        public async Task<IActionResult> DeleteProduct(Guid id)
        {
            var existing = await _passes.GetProduct(id, _tenantContext.TenantId);
            if (existing is null) return new ApiResponses().NotFoundResult("Product not found.");
            try { await _passes.DeleteProduct(id, _tenantContext.TenantId); }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23503")
            {
                return new ApiResponses().BadRequestResult("This pass has purchases on file and can't be deleted. Set inactive instead.");
            }
            return new ApiResponses().OkResult();
        }

        // ── Rider purchase ────────────────────────────────────────────────────────
        [Authorize]
        [HttpPost("Buy")]
        public async Task<IActionResult> Buy([FromBody] BuySeasonPassRequest request, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!_tenantContext.Tenant.SeasonPassesEnabled)
            {
                return new ApiResponses().BadRequestResult("Season passes aren't sold at this track.");
            }
            if (!TryGetUserId(out var userId)) return new ApiResponses().BadRequestResult("Invalid token.");
            var user = await _users.GetById(userId);
            if (user is null) return new ApiResponses().BadRequestResult("User not found.");
            if (_tenantContext.Tenant.RequireEmergencyContact && string.IsNullOrWhiteSpace(user.EmergencyContactPhone))
            {
                return new ApiResponses().BadRequestResult("Please add an emergency contact on your profile before purchasing.");
            }

            // Resolve every line to its product up front so the order is validated as a whole:
            // one dead product shouldn't create half an order and then fail.
            var lines = new List<(SeasonPassProduct Product, int Quantity)>();
            foreach (var item in request.Items)
            {
                if (item.Quantity < 1) return new ApiResponses().BadRequestResult("Pass quantity must be at least 1.");
                var p = await _passes.GetProduct(item.ProductId, _tenantContext.TenantId);
                if (p is null || !p.IsActive) return new ApiResponses().BadRequestResult("Pass is not available.");
                lines.Add((p, item.Quantity));
            }
            if (lines.Count == 0) return new ApiResponses().BadRequestResult("Add at least one pass to continue.");

            var tenant = _tenantContext.Tenant;

            // ── Order pricing ────────────────────────────────────────────────────────
            // Sum the whole order first: a coupon applies once to the order subtotal, not per
            // pass, so we can't price each pass in isolation. Service charge is per-product
            // though — RiderPaidServiceChargeBps varies between products — so the discount is
            // spread across passes below before each one's fee is worked out.
            var subtotalCents = lines.Sum(l => (long)l.Product.PriceCents * l.Quantity);

            CouponApplication? spCoupon = null;
            if (!string.IsNullOrWhiteSpace(request.CouponCode))
            {
                var v = await _couponValidator.ValidateAsync(_tenantContext.TenantId, request.CouponCode!,
                    scope: "season_pass", eventId: null, subtotalCents: (int)subtotalCents, userId: userId);
                if (v.error is not null) return new ApiResponses().BadRequestResult(v.error);
                spCoupon = v.application;
            }
            var orderDiscountCents = spCoupon?.DiscountCents ?? 0;

            // Expand to one row per pass, apportioning the order discount across them by price
            // share. Largest-remainder: hand out the floor of each share, then give the leftover
            // cents to the passes with the biggest fractional part. Every cent of the discount
            // lands on exactly one pass, so the rows always re-add to the charged total — a
            // plain per-pass round would drift by a cent or two and leave the ledger off.
            var passes = new List<(SeasonPassProduct Product, int PriceCents)>();
            foreach (var (product, quantity) in lines)
            {
                for (var i = 0; i < quantity; i++) passes.Add((product, product.PriceCents));
            }

            var discountByPass = new int[passes.Count];
            if (orderDiscountCents > 0 && subtotalCents > 0)
            {
                var remainders = new List<(int Index, long Remainder)>();
                var handedOut = 0;
                for (var i = 0; i < passes.Count; i++)
                {
                    var exact = (long)orderDiscountCents * passes[i].PriceCents;
                    var share = (int)(exact / subtotalCents);
                    discountByPass[i] = share;
                    handedOut += share;
                    remainders.Add((i, exact % subtotalCents));
                }
                foreach (var (index, _) in remainders.OrderByDescending(r => r.Remainder).Take(orderDiscountCents - handedOut))
                {
                    discountByPass[index]++;
                }
            }

            // Price each pass: discounted base, then its product's service charge on top. Clamped
            // at zero so a discount larger than one pass's price can't mint a negative line.
            var priced = new List<(SeasonPassProduct Product, int AmountCents, int ServiceChargeCents, int RiderPortionCents)>();
            for (var i = 0; i < passes.Count; i++)
            {
                var (product, listPrice) = passes[i];
                var basePrice = Math.Max(0, listPrice - discountByPass[i]);
                var serviceCharge = (int)((long)basePrice * tenant.ServiceChargeBps / 10_000L);
                var riderPortion = (int)((long)serviceCharge * product.RiderPaidServiceChargeBps / 10_000L);
                priced.Add((product, basePrice + riderPortion, serviceCharge, riderPortion));
            }

            var amountCents = priced.Sum(p => p.AmountCents);
            var riderPortionTotal = priced.Sum(p => p.RiderPortionCents);

            // Create one pending row per pass. Holder identity, photo, and waiver are deliberately
            // left null: they're collected after payment in CompleteRegistration, and their absence
            // is exactly what stops the gate admitting an unregistered pass.
            var created = new List<(SeasonPassPurchase Purchase, SeasonPassProduct Product)>();
            foreach (var (product, passAmountCents, serviceChargeCents, _) in priced)
            {
                var purchase = new SeasonPassPurchase
                {
                    TenantId = _tenantContext.TenantId,
                    PurchaserUserId = userId,
                    ProductId = product.Id,
                    AmountCents = passAmountCents,
                    ServiceChargeCents = serviceChargeCents,
                    PaymentMethod = "stripe",
                    Status = "pending",
                    PurchaserEmail = user.Email,
                    PurchaserName = $"{user.FirstName} {user.LastName}".Trim(),
                    ValidFromDate = product.ValidFromDate,
                    ValidToDate = product.ValidToDate,
                    CreditsRemaining = product.Kind == "credits" ? product.TotalCredits : null,
                };
                var (newId, newToken) = await _passes.CreatePurchase(purchase);
                purchase.Id = newId;
                purchase.RedemptionToken = newToken;
                created.Add((purchase, product));
            }

            // The order's anchor row: coupon/gift-card redemptions and Stripe metadata are recorded
            // against it, and it's what a single-id lookup (e.g. an old refund path) resolves to.
            var id = created[0].Purchase.Id;

            if (spCoupon is not null)
            {
                await _coupons.RecordRedemption(new CouponRedemption
                {
                    CouponId = spCoupon.Coupon.Id,
                    TenantId = _tenantContext.TenantId,
                    UserId = userId,
                    SourceKind = "season_pass",
                    SourceId = id,
                    DiscountCents = spCoupon.DiscountCents,
                });
            }

            // Gift card: applied AFTER discounts as a payment instrument.
            GiftCardApplication? spGift = null;
            if (!string.IsNullOrWhiteSpace(request.GiftCardCode) && amountCents > 0)
            {
                var gcCheck = await _giftCardValidator.ResolveAsync(_tenantContext.TenantId,
                    request.GiftCardCode!, amountCents);
                if (gcCheck.error is not null) return new ApiResponses().BadRequestResult(gcCheck.error);
                spGift = gcCheck.application;
                // Debit up front with an atomic conditional decrement so concurrent checkouts on
                // the same card can't both spend it; only record the redemption once it succeeds.
                if (!await _giftCards.ApplyToBalance(spGift!.Card.Id, spGift.AmountToApplyCents))
                    return new ApiResponses().BadRequestResult(
                        "That gift card's balance just changed. Please re-apply it and try again.");
                await _giftCards.RecordRedemption(new GiftCardRedemption
                {
                    GiftCardId = spGift.Card.Id,
                    TenantId = _tenantContext.TenantId,
                    UserId = userId,
                    SourceKind = "season_pass",
                    SourceId = id,
                    AmountCents = spGift.AmountToApplyCents,
                });
            }
            var spStripeChargeCents = amountCents - (spGift?.AmountToApplyCents ?? 0);

            // Free fast-path: no PaymentIntent (a gift card fully covered the order, or a coupon zeroed
            // it). The webhook's OnSeasonPassPaid never runs, so record the sale on the ledger here.
            // A gift-card-covered pass is NOT a $0 sale — the buyer paid real money for the card, so the
            // tenant is owed the value; only a coupon-zeroed pass is genuinely free.
            if (spStripeChargeCents == 0)
            {
                var isDirect = _tenantContext.Tenant?.StripeChargeMode == "direct";

                // One ledger entry per pass: source_id is unique per row, and reporting reads the
                // ledger per sale. A single entry for the order would under-count the sale by
                // however many passes came after the first.
                foreach (var (p, _) in created)
                {
                    await _passes.UpdatePurchaseStatus(p.Id, "paid");

                    int gcGross = 0, gcCut = 0, gcNet = 0;
                    if (spGift is not null)
                    {
                        gcGross = p.AmountCents;
                        if (!isDirect)
                        {
                            // Platform holds the gift-card float, so credit the tenant net = gross - cut.
                            var calc = await _feeCalculator.Calculate(_tenantContext.TenantId, p.AmountCents, 0,
                                p.ServiceChargeCents, DateTime.UtcNow, isDirect: false);
                            gcCut = calc.RidepassCutCents;
                            gcNet = calc.NetToTenantCents;
                        }
                        // Direct: float already sits in the tenant's account, our fee was taken at card
                        // sale time, so gross is recorded for reporting but net = 0 / cut = 0.
                    }
                    try
                    {
                        await _ledger.Insert(new TenantLedgerEntry
                        {
                            TenantId = _tenantContext.TenantId,
                            EntryKind = "sale",
                            SourceKind = "season_pass",
                            SourceId = p.Id,
                            OccurredAtUtc = DateTime.UtcNow,
                            GrossCents = gcGross,
                            StripeFeeCents = 0,
                            RidepassCutCents = gcCut,
                            NetToTenantCents = gcNet,
                            PaymentMethod = "voucher",
                            Memo = spGift is not null ? "Gift card covered season pass" : "Coupon covered season pass",
                        });
                    }
                    catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505") { /* idempotent */ }
                }
                return new ApiResponses().OkResult(new BuySeasonPassResponse
                {
                    Passes = ToPurchaseItems(created),
                    ClientSecret = string.Empty,
                    AmountCents = 0,
                    RiderServiceChargeCents = 0,
                    GiftCardAppliedCents = spGift?.AmountToApplyCents ?? 0,
                });
            }

            // season_pass_id names the anchor row only; the finalizer resolves the full set by
            // payment intent id, so an order of several passes doesn't need them all in metadata
            // (Stripe caps metadata values at 500 chars, which a long enough order would blow).
            var metadata = new Dictionary<string, string>
            {
                ["tenant_id"] = _tenantContext.TenantId.ToString(),
                ["season_pass_id"] = id.ToString(),
                ["season_pass_count"] = created.Count.ToString(),
                ["user_id"] = userId.ToString(),
                ["sale_kind"] = "season_pass",
            };
            if (spGift is not null)
            {
                metadata["gift_card_applied_cents"] = spGift.AmountToApplyCents.ToString();
                metadata["gift_card_id"] = spGift.Card.Id.ToString();
            }

            // Direct-charge tenants charge on their own connected account; our service fee rides as
            // the Stripe application fee. The fee is planned on the ORDER's service charge, since
            // one intent covers every pass.
            var serviceChargeTotal = priced.Sum(p => p.ServiceChargeCents);
            PaymentIntentCreated intent;
            ChargePlan chargePlan;
            try
            {
                chargePlan = _chargeRouter.Plan(tenant, serviceChargeTotal, spStripeChargeCents);
                intent = await _payments.CreatePaymentIntentAsync(
                    spStripeChargeCents, "usd", metadata, user.Email,
                    connectedAccountId: chargePlan.ConnectedAccountId,
                    applicationFeeCents: chargePlan.ApplicationFeeCents, ct: ct);
            }
            catch (InvalidOperationException ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }

            // Every pass on the order points at the one intent — that's what lets the finalizer
            // find them all when the charge settles.
            foreach (var (p, _) in created)
            {
                await _passes.SetPurchaseStripePaymentIntentId(p.Id, intent.IntentId);
                if (chargePlan.IsDirect)
                {
                    await _passes.MarkPurchaseDirectCharge(p.Id, _tenantContext.TenantId, chargePlan.ConnectedAccountId!);
                }
            }

            return new ApiResponses().OkResult(new BuySeasonPassResponse
            {
                Passes = ToPurchaseItems(created),
                ClientSecret = intent.ClientSecret,
                AmountCents = spStripeChargeCents,
                RiderServiceChargeCents = riderPortionTotal,
                GiftCardAppliedCents = spGift?.AmountToApplyCents ?? 0,
            });
        }

        // ── Post-payment registration ─────────────────────────────────────────────
        // Payment buys the passes; this names who each one admits and captures the photo +
        // waiver the gate checks. Split from Buy so a buyer isn't filling four photo dialogs
        // before they know the card even cleared. Until this runs the pass is paid but not
        // admissible — see CheckIn.
        [Authorize]
        [HttpPost("CompleteRegistration")]
        public async Task<IActionResult> CompleteRegistration([FromBody] CompleteSeasonPassRegistrationRequest request)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!TryGetUserId(out var userId)) return new ApiResponses().BadRequestResult("Invalid token.");
            if (request.Passes.Count == 0) return new ApiResponses().BadRequestResult("No passes to register.");

            // Reject duplicate pass ids: two entries for one pass would silently apply whichever
            // landed last, so the buyer would think they'd registered two holders.
            if (request.Passes.Select(p => p.PurchaseId).Distinct().Count() != request.Passes.Count)
            {
                return new ApiResponses().BadRequestResult("Each pass can only be registered once per request.");
            }

            // Validate the whole batch before writing anything: a half-registered order (two of four
            // holders saved, then a validation failure) would leave the buyer re-entering details
            // for passes that already took.
            var toWrite = new List<(SeasonPassRegistrationItem Item, SeasonPassPurchase Pass, SeasonPassProduct Product, Guid? WaiverId)>();
            foreach (var item in request.Passes)
            {
                var pass = await _passes.GetPurchase(item.PurchaseId);
                // Same 400 for missing / wrong-tenant / someone else's pass: a distinct "not yours"
                // would confirm the id exists in another account.
                if (pass is null || pass.TenantId != _tenantContext.TenantId || pass.PurchaserUserId != userId)
                {
                    return new ApiResponses().BadRequestResult("Pass not found.");
                }
                if (pass.Status != "paid")
                {
                    return new ApiResponses().BadRequestResult(
                        "This pass isn't paid yet. If you were charged, your confirmation will arrive by email.");
                }

                var product = await _passes.GetProduct(pass.ProductId, _tenantContext.TenantId);
                if (product is null) return new ApiResponses().BadRequestResult("Pass is not available.");

                if (string.IsNullOrWhiteSpace(item.FirstName) || string.IsNullOrWhiteSpace(item.LastName))
                {
                    return new ApiResponses().BadRequestResult("Each pass needs the holder's first and last name.");
                }
                if (!IsValidPhotoDataUrl(item.PhotoDataUrl))
                {
                    return new ApiResponses().BadRequestResult(
                        "A photo of the pass holder is required for ID verification at the gate.");
                }

                // The waiver is only enforceable when the tenant actually has one published —
                // matching WaiverCheckInGate, which treats a missing document as nothing to
                // enforce rather than an unpassable gate.
                Guid? waiverId = null;
                if (product.RequiresWaiver)
                {
                    waiverId = (await _waivers.GetActive(_tenantContext.TenantId))?.Id;
                    if (waiverId is not null)
                    {
                        if (string.IsNullOrWhiteSpace(item.WaiverSignatureDataUrl))
                        {
                            return new ApiResponses().BadRequestResult(
                                $"{item.FirstName.Trim()} needs to sign the waiver before this pass can be used.");
                        }
                        // Birthdate decides whether a parent/guardian must sign, so a blank one would
                        // let a minor be signed in as an adult.
                        if (item.Birthdate is null)
                        {
                            return new ApiResponses().BadRequestResult(
                                $"{item.FirstName.Trim()} needs a date of birth to sign the waiver.");
                        }
                        if (WaiverPolicy.IsMinor(item.Birthdate) && string.IsNullOrWhiteSpace(item.ParentGuardianName))
                        {
                            return new ApiResponses().BadRequestResult(
                                $"A parent/guardian name is required for {item.FirstName.Trim()}.");
                        }
                    }
                }

                toWrite.Add((item, pass, product, waiverId));
            }

            foreach (var (item, pass, product, waiverId) in toWrite)
            {
                // One signature row per holder in the shared rider_waiver_signature store, linked
                // from the pass — the same source of truth the gate and the "who has signed"
                // report read for event tickets. Not keyed to a user account: the holder is often
                // a child with no login of their own.
                Guid? signatureId = null;
                if (waiverId is not null && !string.IsNullOrWhiteSpace(item.WaiverSignatureDataUrl))
                {
                    var isMinorHolder = WaiverPolicy.IsMinor(item.Birthdate);
                    var holderName = $"{item.FirstName.Trim()} {item.LastName.Trim()}".Trim();
                    var signerName = isMinorHolder && !string.IsNullOrWhiteSpace(item.ParentGuardianName)
                        ? item.ParentGuardianName!.Trim()
                        : holderName;
                    var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
                    signatureId = await _waivers.SignRegistrant(_tenantContext.TenantId, waiverId.Value, ip,
                        item.WaiverSignatureDataUrl!, signerEmail: pass.PurchaserEmail, signerName: signerName,
                        attendeeFirstName: item.FirstName.Trim(), attendeeLastName: item.LastName.Trim(),
                        attendeeBirthdate: item.Birthdate,
                        signedByParent: isMinorHolder,
                        parentName: isMinorHolder ? item.ParentGuardianName?.Trim() : null,
                        parentPhone: isMinorHolder ? item.ParentGuardianPhone?.Trim() : null);
                }

                var affected = await _passes.CompleteRegistration(pass.Id, _tenantContext.TenantId, userId,
                    item.FirstName.Trim(), item.LastName.Trim(), item.Birthdate,
                    item.PhotoDataUrl, signatureId);
                if (affected == 0)
                {
                    // The row moved between validation and write (refunded/cancelled mid-flow).
                    return new ApiResponses().BadRequestResult(
                        "This pass can no longer be registered. It may have been refunded or cancelled.");
                }
            }

            return new ApiResponses().OkResult();
        }

        private static List<SeasonPassPurchaseItem> ToPurchaseItems(
            IEnumerable<(SeasonPassPurchase Purchase, SeasonPassProduct Product)> created) =>
            created.Select(c => new SeasonPassPurchaseItem
            {
                PurchaseId = c.Purchase.Id,
                RedemptionToken = c.Purchase.RedemptionToken,
                ProductId = c.Product.Id,
                ProductName = c.Product.Name,
                RequiresWaiver = c.Product.RequiresWaiver,
            }).ToList();

        [Authorize]
        [HttpGet("Mine")]
        public async Task<IActionResult> ListMine()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!TryGetUserId(out var userId)) return new ApiResponses().BadRequestResult("Invalid token.");
            var rows = await _passes.ListMine(userId, _tenantContext.TenantId);
            return new ApiResponses().OkResult(rows.Select(r => new MySeasonPassResponse
            {
                Id = r.Id,
                RedemptionToken = r.RedemptionToken,
                ProductName = r.ProductName,
                ProductKind = r.ProductKind,
                CreditsRemaining = r.CreditsRemaining,
                ValidDaysOfWeek = r.ProductValidDaysOfWeek,
                ValidFromDate = r.ValidFromDate,
                ValidToDate = r.ValidToDate,
                Status = r.Status,
                RequiresWaiver = r.ProductRequiresWaiver,
                RegistrationComplete = r.IsRegistered(),
                HolderFirstName = r.HolderFirstName,
                HolderLastName = r.HolderLastName,
                CreatedAtUtc = DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc),
            }));
        }

        [Authorize]
        [HttpPost("Reserve")]
        public async Task<IActionResult> Reserve([FromBody] SeasonPassReserveRequest request)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!TryGetUserId(out var userId)) return new ApiResponses().BadRequestResult("Invalid token.");

            var pass = await _passes.GetPurchase(request.PassPurchaseId);
            if (pass is null || pass.PurchaserUserId != userId)
            {
                return new ApiResponses().BadRequestResult("That pass isn't yours.");
            }
            if (pass.TenantId != _tenantContext.TenantId)
            {
                return new ApiResponses().BadRequestResult("That pass belongs to a different track.");
            }
            if (pass.Status != "paid")
            {
                return new ApiResponses().BadRequestResult("This pass isn't active yet.");
            }
            var ev = await _events.GetById(request.EventId, _tenantContext.TenantId);
            if (ev is null || ev.Status != "scheduled" || ev.EndsAt < DateTime.UtcNow)
            {
                return new ApiResponses().BadRequestResult("That event isn't available.");
            }
            if (ev.StartsAt.Date < pass.ValidFromDate.Date || ev.StartsAt.Date > pass.ValidToDate.Date)
            {
                return new ApiResponses().BadRequestResult("This pass isn't valid on the event's date.");
            }

            var product = await _passes.GetProduct(pass.ProductId, _tenantContext.TenantId);
            if (product is null) return new ApiResponses().BadRequestResult("Pass product missing — contact the tenant.");
            if (product.Kind == "days_of_week" && product.ValidDaysOfWeek is { Length: > 0 })
            {
                var dow = (int)ev.StartsAt.DayOfWeek;       // 0=Sun..6=Sat
                if (!product.ValidDaysOfWeek.Contains(dow))
                {
                    return new ApiResponses().BadRequestResult("This pass isn't valid on that day of the week.");
                }
            }
            if (product.Kind == "credits" && (pass.CreditsRemaining ?? 0) <= 0)
            {
                return new ApiResponses().BadRequestResult("This pass has no credits remaining.");
            }

            // Check capacity (season-pass reservations against the event capacity).
            if (ev.Capacity.HasValue)
            {
                var seasonReserved = (await _passes.ActiveReservationsForEvents(new[] { request.EventId }))
                    .GetValueOrDefault(request.EventId, 0);
                if (seasonReserved >= ev.Capacity.Value)
                {
                    return new ApiResponses().BadRequestResult("This event is sold out.");
                }
            }

            var existing = await _passes.GetReservation(request.PassPurchaseId, request.EventId);
            if (existing is not null && existing.Status != "cancelled")
            {
                return new ApiResponses().OkResult(new { reservationId = existing.Id, alreadyReserved = true });
            }

            var reservationId = await _passes.CreateReservation(new SeasonPassReservation
            {
                SeasonPassPurchaseId = request.PassPurchaseId,
                EventId = request.EventId,
                Status = "reserved",
            });
            if (product.Kind == "credits") await _passes.DecrementCredits(pass.Id);
            return new ApiResponses().OkResult(new { reservationId, alreadyReserved = false });
        }

        // ── Gate check-in: staff scans a pass QR ─────────────────────────────────
        [Authorize(Policy = TenantPermissions.Policy.SalesRedeem)]
        [HttpGet("Pass/{token:guid}")]
        public async Task<IActionResult> LookupPassByToken(Guid token)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var pass = await _passes.GetPurchaseByRedemptionToken(token);
            if (pass is null || pass.TenantId != _tenantContext.TenantId)
            {
                return new ApiResponses().NotFoundResult("Pass not found.");
            }
            var product = await _passes.GetProduct(pass.ProductId, _tenantContext.TenantId);
            // Today's reservations in tenant tz, expressed as a UTC range.
            var tz = TimeZoneInfo.FindSystemTimeZoneById(_tenantContext.Tenant.Timezone);
            var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
            var dayStartLocal = nowLocal.Date;
            var atUtc = TimeZoneInfo.ConvertTimeToUtc(dayStartLocal, tz);
            var untilUtc = TimeZoneInfo.ConvertTimeToUtc(dayStartLocal.AddDays(1), tz);
            var reservations = await _passes.ListReservationsForPurchaseOnDate(pass.Id, atUtc, untilUtc);

            // Registration state up front so staff see "not registered yet" on the scan rather than
            // only when the check-in button fails.
            var registrationComplete = pass.IsRegistered(product?.RequiresWaiver ?? false);

            return new ApiResponses().OkResult(new
            {
                pass.Id,
                pass.PurchaserName,
                pass.PurchaserEmail,
                pass.Status,
                pass.ValidFromDate,
                pass.ValidToDate,
                pass.CreditsRemaining,
                pass.PhotoDataUrl,
                // Who the pass admits — may differ from the buyer, so this is the name staff
                // should be checking against the photo.
                HolderName = string.IsNullOrWhiteSpace(pass.HolderFirstName)
                    ? null
                    : $"{pass.HolderFirstName} {pass.HolderLastName}".Trim(),
                RegistrationComplete = registrationComplete,
                ProductName = product?.Name,
                ProductKind = product?.Kind,
                ValidDaysOfWeek = product?.ValidDaysOfWeek,
                TodaysReservations = reservations.Select(r => new
                {
                    r.Id,
                    r.EventId,
                    r.EventTitle,
                    EventStartsAtUtc = DateTime.SpecifyKind(r.EventStartsAt, DateTimeKind.Utc),
                    EventEndsAtUtc = DateTime.SpecifyKind(r.EventEndsAt, DateTimeKind.Utc),
                    r.Status,
                    CheckedInAtUtc = r.CheckedInAt is null ? null : (DateTime?)DateTime.SpecifyKind(r.CheckedInAt.Value, DateTimeKind.Utc),
                }),
            });
        }

        /// <summary>
        /// Why this pass can't be admitted yet, or null when it's good to go. Checkout deliberately
        /// creates a pass before its holder is known (pay first, register after), so a paid pass is
        /// not automatically an admissible one: the gate needs a face to check against and, when the
        /// product requires it, a signed waiver. Returns staff-readable copy naming what's missing.
        /// </summary>
        private async Task<string?> RegistrationBlockReason(SeasonPassCheckInContext ctx)
        {
            var who = string.IsNullOrWhiteSpace(ctx.HolderFirstName)
                ? (string.IsNullOrWhiteSpace(ctx.HolderName) ? "This pass holder" : ctx.HolderName!)
                : ctx.HolderFirstName!;

            // No photo means the pass was never registered (checkout writes the holder, photo, and
            // waiver together), so this covers "not registered at all" too.
            if (!ctx.HasPhoto)
            {
                return "This pass hasn't been registered yet, so there's no photo to verify the holder "
                     + "against. The buyer needs to finish registration (holder details, photo"
                     + (ctx.ProductRequiresWaiver ? ", and waiver" : string.Empty) + ") before it can be used.";
            }
            if (ctx.ProductRequiresWaiver && ctx.WaiverSignatureId is null)
            {
                // Nothing to enforce when the tenant has published no waiver document — same
                // misconfiguration escape hatch WaiverCheckInGate uses, so a track that flags a
                // product as waiver-required but never writes a waiver isn't left with an
                // unpassable gate.
                var activeWaiver = await _waivers.GetActive(_tenantContext.TenantId);
                if (activeWaiver is not null)
                {
                    return $"This pass requires a signed waiver. {who} must sign before checking in.";
                }
            }
            return null;
        }

        private static bool IsValidPhotoDataUrl(string? dataUrl)
        {
            if (string.IsNullOrWhiteSpace(dataUrl)) return false;
            // Accept JPEG or PNG; bound size to discourage abuse but permit a reasonable selfie.
            if (!(dataUrl.StartsWith("data:image/jpeg;base64,", StringComparison.Ordinal)
                  || dataUrl.StartsWith("data:image/png;base64,", StringComparison.Ordinal))) return false;
            return dataUrl.Length is > 1_000 and < 2_000_000;
        }

        [Authorize(Policy = TenantPermissions.Policy.SalesRedeem)]
        [HttpPost("Reservations/{id:guid}/CheckIn")]
        public async Task<IActionResult> CheckIn(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            // Tenant scope is enforced inside UpdateReservationStatus by joining
            // season_pass_reservation → season_pass_purchase and filtering by
            // tenant_id, so a staff JWT scoped to tenant A can't flip a
            // reservation that belongs to tenant B.
            Guid? staffId = TryGetUserId(out var sid) ? sid : (Guid?)null;

            var ctx = await _passes.GetReservationForCheckIn(id, _tenantContext.TenantId);
            if (ctx is not null)
            {
                var registrationBlock = await RegistrationBlockReason(ctx);
                if (registrationBlock is not null) return new ApiResponses().BadRequestResult(registrationBlock);

                // A required EVENT waiver can't be skipped at the season-pass gate either (it's a
                // separate requirement from the pass product's own waiver — an event can pin its
                // own document). The holder is a rider, so enforce the event's rider waiver.
                //
                // Only when the pass carries no signature of its own: BlockReason keys on the
                // purchaser's account, and a pass bought FOR someone else would then be tested
                // against the buyer's signature and wave the actual holder through. When the pass
                // does have a holder signature, that signature is the holder's own evidence — the
                // same standard BlockReasonForTicket applies to an event ticket.
                if (ctx.WaiverSignatureId is null)
                {
                    var waiverBlock = await _waiverGate.BlockReason(_tenantContext.TenantId, ctx.EventId,
                        riderAudience: true, ctx.HolderUserId, ctx.HolderEmail, ctx.HolderName);
                    if (waiverBlock is not null) return new ApiResponses().BadRequestResult(waiverBlock);
                }
            }

            var affected = await _passes.UpdateReservationStatus(id, _tenantContext.TenantId, "checked_in", staffId);
            if (affected == 0)
                return new ApiResponses().BadRequestResult(
                    "This pass can't be checked in. It may be refunded, cancelled, or already checked in.");
            return new ApiResponses().OkResult();
        }

        private bool TryGetUserId(out Guid userId)
        {
            var claim = User.FindFirst("UserId")?.Value;
            return Guid.TryParse(claim, out userId);
        }

        /// <summary>
        /// Projects a list of products in a fixed number of queries. The per-product ToResponse
        /// would run two lookups each, and this backs the public landing page, so a track with a
        /// handful of passes would issue a dozen round-trips to render one page.
        /// </summary>
        private async Task<List<SeasonPassProductResponse>> ToResponses(List<SeasonPassProduct> products)
        {
            if (products.Count == 0) return new List<SeasonPassProductResponse>();
            var benefitsByProduct = await _passes.ListBenefitsForProducts(
                products.Select(p => p.Id), _tenantContext.TenantId);
            var eventTypeNames = (await _eventTypes.GetAllForTenant(_tenantContext.TenantId))
                .ToDictionary(t => t.Id, t => t.Name);

            return products.Select(p =>
            {
                var benefits = benefitsByProduct.GetValueOrDefault(p.Id) ?? new List<SeasonPassBenefit>();
                return new SeasonPassProductResponse
                {
                    Id = p.Id,
                    Name = p.Name,
                    Description = p.Description,
                    PriceCents = p.PriceCents,
                    ValidFromDate = p.ValidFromDate,
                    ValidToDate = p.ValidToDate,
                    Kind = p.Kind,
                    ValidDaysOfWeek = p.ValidDaysOfWeek,
                    TotalCredits = p.TotalCredits,
                    RequiresWaiver = p.RequiresWaiver,
                    RiderPaidServiceChargeBps = p.RiderPaidServiceChargeBps,
                    IsActive = p.IsActive,
                    SortOrder = p.SortOrder,
                    // Legacy shape, still emitted for older clients. Percent benefits only —
                    // an 'amount' benefit has no faithful representation as a percent.
                    Perks = benefits
                        .Where(b => b.BenefitType == "event" && b.ScopeId.HasValue && b.DiscountKind == "percent")
                        .Select(b => new EventTypePerkInput
                        {
                            EventTypeId = b.ScopeId!.Value,
                            DiscountPercent = b.DiscountValue / 100,
                        }).ToList(),
                    Benefits = benefits.Select(b => new SeasonPassBenefitInput
                    {
                        BenefitType = b.BenefitType,
                        ScopeId = b.ScopeId,
                        DiscountKind = b.DiscountKind,
                        DiscountValue = b.DiscountValue,
                        Quantity = b.Quantity,
                        ScopeName = b.BenefitType == "event" && b.ScopeId.HasValue
                            ? eventTypeNames.GetValueOrDefault(b.ScopeId.Value)
                            : null,
                    }).ToList(),
                };
            }).ToList();
        }

        private async Task<SeasonPassProductResponse> ToResponse(SeasonPassProduct p) =>
            (await ToResponses(new List<SeasonPassProduct> { p }))[0];
    }
}
