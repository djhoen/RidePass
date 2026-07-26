using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Coupons;
using Services.Helpers;
using Services.Helpers.Interfaces;
using Services.Payments;
using Services.Repositories.Data.CouponData;
using Services.Repositories.Data.ExtrasData;
using Services.Repositories.Data.GiftCardData;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Interfaces;
using Services.LoamPassMx;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Extras;
using webapi.Controllers.API.Data.Purchase;
using webapi.Helpers;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PurchaseController : ControllerBase
    {
        private readonly IWaiverRepository _waivers;
        private readonly IUserRepository _users;
        private readonly IEventRepository _events;
        private readonly IEventTicketTierRepository _tiers;
        private readonly IEventTicketPurchaseRepository _ticketPurchases;
        private readonly IDisputeRepository _disputes;
        private readonly IPaymentProvider _payments;
        private readonly IChargeRouter _chargeRouter;
        private readonly ITenantLedgerRepository _ledger;
        private readonly ITenantCreditRepository _credit;
        private readonly Services.Audit.IAuditLogger _audit;
        private readonly ICouponRepository _coupons;
        private readonly ICouponValidator _couponValidator;
        private readonly IGiftCardRepository _giftCards;
        private readonly Services.GiftCards.IGiftCardValidator _giftCardValidator;
        private readonly Services.Waitlist.IWaitlistPromoter _waitlistPromoter;
        private readonly IEventExtraRepository _extras;
        private readonly IBikeShopRepository _shop;
        private readonly IInstructorRepository _instructors;
        private readonly IMembershipRepository _memberships;
        private readonly IRecentSalesRepository _recentSales;
        private readonly IDbHelper _db;
        private readonly ITenantContext _tenantContext;
        private readonly ITenantEventTypeRepository _eventTypes;
        private readonly IRiderLoampassLinkRepository _loampassLinks;
        private readonly ILoamPassMxService _loampass;
        private readonly ILoampassRedemptionRepository _loampassRedemptions;
        private readonly Services.Email.IEventOrderConfirmationEmailer _orderConfirmations;
        private readonly ISeasonPassRepository _seasonPasses;
        private readonly ITenantTaxRepository _tax;
        private readonly Services.Payments.IFeeCalculator _feeCalculator;

        public PurchaseController(
            IWaiverRepository waivers,
            IUserRepository users,
            IEventRepository events,
            IEventTicketTierRepository tiers,
            IEventTicketPurchaseRepository ticketPurchases,
            IDisputeRepository disputes,
            IPaymentProvider payments,
            ITenantLedgerRepository ledger,
            ICouponRepository coupons,
            ICouponValidator couponValidator,
            IGiftCardRepository giftCards,
            Services.GiftCards.IGiftCardValidator giftCardValidator,
            Services.Waitlist.IWaitlistPromoter waitlistPromoter,
            IEventExtraRepository extras,
            IBikeShopRepository shop,
            IInstructorRepository instructors,
            IMembershipRepository memberships,
            IRecentSalesRepository recentSales,
            IDbHelper db,
            ITenantContext tenantContext,
            ITenantEventTypeRepository eventTypes,
            IRiderLoampassLinkRepository loampassLinks,
            ILoamPassMxService loampass,
            ILoampassRedemptionRepository loampassRedemptions,
            Services.Email.IEventOrderConfirmationEmailer orderConfirmations,
            IChargeRouter chargeRouter,
            ISeasonPassRepository seasonPasses,
            ITenantTaxRepository tax,
            Services.Payments.IFeeCalculator feeCalculator,
            ITenantCreditRepository credit,
            Services.Audit.IAuditLogger audit)
        {
            _audit = audit;
            _credit = credit;
            _waivers = waivers;
            _users = users;
            _events = events;
            _tiers = tiers;
            _ticketPurchases = ticketPurchases;
            _disputes = disputes;
            _payments = payments;
            _ledger = ledger;
            _coupons = coupons;
            _couponValidator = couponValidator;
            _giftCards = giftCards;
            _giftCardValidator = giftCardValidator;
            _waitlistPromoter = waitlistPromoter;
            _extras = extras;
            _shop = shop;
            _instructors = instructors;
            _memberships = memberships;
            _recentSales = recentSales;
            _db = db;
            _tenantContext = tenantContext;
            _eventTypes = eventTypes;
            _loampassLinks = loampassLinks;
            _loampass = loampass;
            _loampassRedemptions = loampassRedemptions;
            _orderConfirmations = orderConfirmations;
            _chargeRouter = chargeRouter;
            _seasonPasses = seasonPasses;
            _tax = tax;
            _feeCalculator = feeCalculator;
        }

        // Loads the tenant's admission tax config (once per checkout). Returns None when no active,
        // non-zero rate is configured, so the math below is a no-op for tenants without tax.
        private async Task<AdmissionTaxConfig> LoadAdmissionTaxConfig(Guid tenantId)
        {
            var row = await _tax.GetByKind(tenantId, "admission");
            return row is { IsActive: true, RateBps: > 0 }
                ? new AdmissionTaxConfig(row.RateBps, row.PricesIncludeTax, row.ServiceChargeTaxable)
                : AdmissionTaxConfig.None;
        }

        // Mirrors ExtraController.ResolveVariantOrError. Variants short-circuit the
        // per-event eligibility inventory cap with their own tenant-wide stock, and
        // their PriceCents (when set) overrides the product's.
        private record ExtraVariantResolveResult(EventExtraVariant? Variant, int UnitPriceCents, string? Error);
        private async Task<ExtraVariantResolveResult> ResolveExtraVariant(
            EventExtraProduct product, BuyExtrasItem item, EventExtraEligibility eligibility)
        {
            // Expiry → sold-out look (admin can re-extend the date).
            if (product.ExpiresAt.HasValue && product.ExpiresAt.Value <= DateTime.UtcNow)
            {
                return new(null, 0, $"\"{product.Name}\" is no longer being sold.");
            }
            // Tenant-wide product cap. Layered before variant / per-event checks so the
            // tighter constraint surfaces first to the buyer.
            if (product.Inventory.HasValue)
            {
                var soldProduct = await _extras.SumSoldProduct(product.Id);
                var remainingProduct = product.Inventory.Value - soldProduct;
                if (item.Quantity > remainingProduct)
                {
                    return new(null, 0, remainingProduct <= 0
                        ? $"\"{product.Name}\" is sold out."
                        : $"Only {remainingProduct} of \"{product.Name}\" left.");
                }
            }

            var variants = await _extras.ListVariants(product.Id);
            var activeVariants = variants.Where(v => v.IsActive).ToList();
            if (activeVariants.Count > 0)
            {
                if (!item.VariantId.HasValue)
                {
                    return new(null, 0, $"Pick a size/color/gender for \"{product.Name}\".");
                }
                var variant = activeVariants.FirstOrDefault(v => v.Id == item.VariantId.Value);
                if (variant is null)
                {
                    return new(null, 0, $"That option isn't available for \"{product.Name}\".");
                }
                if (variant.Inventory.HasValue)
                {
                    var sold = await _extras.SumSoldVariant(variant.Id);
                    var remaining = variant.Inventory.Value - sold;
                    if (item.Quantity > remaining)
                    {
                        var label = string.Join(" / ",
                            new[] { variant.Size, variant.Color, variant.Gender }.Where(s => !string.IsNullOrWhiteSpace(s)));
                        var qual = string.IsNullOrWhiteSpace(label) ? product.Name : $"{product.Name} ({label})";
                        return new(null, 0, remaining <= 0 ? $"\"{qual}\" is sold out." : $"Only {remaining} of \"{qual}\" left.");
                    }
                }
                var unit = variant.PriceCents ?? product.PriceCents;
                return new(variant, unit, null);
            }

            // Legacy single-SKU path: per-event inventory cap.
            if (eligibility.Inventory.HasValue)
            {
                var sold = await _extras.SumSold(eligibility.EventId, product.Id);
                var remaining = eligibility.Inventory.Value - sold;
                if (item.Quantity > remaining)
                {
                    return new(null, 0, remaining <= 0
                        ? $"\"{product.Name}\" is sold out for this event."
                        : $"Only {remaining} of \"{product.Name}\" left at this event.");
                }
            }
            return new(null, product.PriceCents, null);
        }

        [AllowAnonymous]
        [HttpPost("EventTicket")]
        public async Task<IActionResult> BuyEventTicket([FromBody] CreateTicketPurchaseRequest request, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved)
            {
                return new ApiResponses().BadRequestResult("No tenant resolved.");
            }

            // Auth is optional for event tickets — guests can buy without an account.
            Guid? purchaserUserId = null;
            string purchaserEmail;
            string purchaserName;

            if (User.Identity?.IsAuthenticated == true && TryGetUserId(out var userId))
            {
                var user = await _users.GetById(userId);
                if (user is null || (user.TenantId.HasValue && user.TenantId != _tenantContext.TenantId))
                {
                    return new ApiResponses().BadRequestResult("User not found in this tenant.");
                }
                purchaserUserId = userId;
                purchaserEmail = user.Email;
                purchaserName = $"{user.FirstName} {user.LastName}".Trim();
            }
            else
            {
                if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Name))
                {
                    return new ApiResponses().BadRequestResult("Email and name are required for guest checkout.");
                }
                purchaserEmail = request.Email.Trim();
                purchaserName = request.Name.Trim();
            }

            // Cart validation: dedupe by tier (silently sum quantities), reject empties.
            var items = (request.Items ?? new List<TicketCartItem>())
                .Where(i => i.Quantity > 0)
                .GroupBy(i => i.TierId)
                .Select(g => new TicketCartItem { TierId = g.Key, Quantity = g.Sum(x => x.Quantity) })
                .ToList();
            if (items.Count == 0)
            {
                return new ApiResponses().BadRequestResult("Cart is empty.");
            }
            var totalUnits = items.Sum(i => i.Quantity);

            // Load and validate every tier; they must all belong to the same event so we
            // can run waiver / event-status / capacity checks once for the whole cart.
            var tierLookup = new Dictionary<Guid, Services.Repositories.Data.PaymentData.EventTicketTier>();
            Guid? sharedEventId = null;
            foreach (var item in items)
            {
                var tier = await _tiers.GetById(item.TierId, _tenantContext.TenantId);
                if (tier is null || !tier.IsActive)
                {
                    return new ApiResponses().BadRequestResult("One of the selected admissions is no longer available.");
                }
                if (sharedEventId is null) sharedEventId = tier.EventId;
                else if (sharedEventId != tier.EventId)
                {
                    return new ApiResponses().BadRequestResult("All admissions in a single purchase must be for the same event.");
                }
                // Per-tier inventory only applies to standalone tiers. Ladder steps are
                // capped by event.capacity at the group level (handled after the event loads).
                if (tier.PartySizeMax is int partyMax && item.Quantity > partyMax)
                {
                    return new ApiResponses().BadRequestResult(
                        $"'{tier.Name}' covers up to {partyMax} rider(s) per booking.");
                }
                var preCap = tier.LadderGroup is null ? await EffectiveTierCap(tier) : null;
                if (preCap is int preCapValue)
                {
                    var sold = await _tiers.SoldCount(tier.Id);
                    if (sold + item.Quantity > preCapValue)
                    {
                        var remaining = Math.Max(0, preCapValue - sold);
                        return new ApiResponses().BadRequestResult(
                            $"Not enough '{tier.Name}' left ({remaining} remaining, you asked for {item.Quantity}).");
                    }
                }
                // Race classes are one-per-rider. In the legacy/POS path the purchaser IS
                // the rider, so we enforce uniqueness here. In the deferred unified-checkout
                // path a buyer can enter the same class for several different riders, so this
                // moves to CompleteRegistration (uniqueness per registrant, not per buyer).
                if (tier.Kind == "race_entry" && !request.DeferRegistration)
                {
                    if (item.Quantity > 1)
                    {
                        return new ApiResponses().BadRequestResult(
                            $"You can only enter '{tier.Name}' once.");
                    }
                    var already = await _ticketPurchases.HasActiveRaceEntry(
                        _tenantContext.TenantId, tier.Id, purchaserUserId, purchaserEmail);
                    if (already)
                    {
                        return new ApiResponses().BadRequestResult(
                            $"You're already entered in '{tier.Name}' — riders can only enter each class once.");
                    }
                }
                tierLookup[tier.Id] = tier;
            }

            // Emergency contact gate. In DeferRegistration (unified checkout) mode this is
            // collected per rider AFTER payment in CompleteTicketRegistration, so the buyer
            // can pay first and finish rider details after, so don't block here. Only the
            // legacy/POS path (purchaser IS the rider, no post-payment registration step)
            // still enforces it up front, mirroring the waiver gate below.
            if (!request.DeferRegistration && purchaserUserId.HasValue && _tenantContext.Tenant.RequireEmergencyContact)
            {
                var buyer = await _users.GetById(purchaserUserId.Value);
                if (buyer is not null && string.IsNullOrWhiteSpace(buyer.EmergencyContactPhone))
                {
                    return new ApiResponses().BadRequestResult("Please add an emergency contact on your profile before purchasing.");
                }
            }

            var parentEvent = await _events.GetById(sharedEventId!.Value, _tenantContext.TenantId);
            if (parentEvent is null || parentEvent.Status != "scheduled" || parentEvent.EndsAt < DateTime.UtcNow)
            {
                return new ApiResponses().BadRequestResult("That event has already ended or is no longer available.");
            }

            // Active tiers for this event, loaded once (used by the price-ladder + gate-fee checks).
            var eventTiers = await _tiers.GetForEvent(parentEvent.Id, _tenantContext.TenantId, activeOnly: true);

            // Dynamic price-ladder enforcement. A ladder step in the cart must be the *active*
            // (cheapest fired) step; if sales or a date moved the price since the page loaded,
            // return 409 price_changed so the client re-confirms. The whole order is honored at
            // the active price implicitly (every ticket references the active step's tier and
            // freezes its price). Capacity is NOT checked here: a ladder carries no per-step cap
            // and sells against event.capacity, which the event-wide check below owns for every
            // tier at once.
            // NOTE: like the rest of the ticket path this is read-then-insert (no advisory lock),
            // so under heavy concurrency a few extra sales could land at the lower step. Matches
            // the existing inventory check's posture; tighten with an advisory lock if needed.
            foreach (var groupId in items
                         .Select(i => tierLookup[i.TierId].LadderGroup)
                         .Where(g => g is not null)
                         .Distinct())
            {
                var steps = eventTiers.Where(t => t.LadderGroup == groupId).ToList();
                var groupSold = await _tiers.GroupSoldCount(parentEvent.Id, groupId!, _tenantContext.TenantId);
                var state = Services.Pricing.PriceStepResolver.Resolve(
                    steps, groupSold, parentEvent.StartsAt, DateTime.UtcNow);
                if (state is null)
                {
                    return new ApiResponses().BadRequestResult("This ticket isn't available right now.");
                }
                var groupItems = items.Where(i => tierLookup[i.TierId].LadderGroup == groupId).ToList();
                if (groupItems.Any(i => i.TierId != state.Active.Id))
                {
                    return StatusCode(409, new
                    {
                        code = "price_changed",
                        activeTierId = state.Active.Id,
                        priceCents = state.Active.PriceCents,
                        message = $"The price for \"{state.Active.Name}\" is now ${state.Active.PriceCents / 100m:0.00}. Please review and confirm.",
                    });
                }
            }

            // Event-wide capacity, the single owner of event.capacity for every tier shape.
            // It replaced a per-ladder-group check that could only ever run for ladder steps, so
            // a cart of plain tiers went completely unbounded. Counts rider admissions only
            // (spectators don't consume rider capacity) across every tier, which also makes it
            // the correct outer bound when one event runs several ladder groups at once.
            // Fast-fail here; the authoritative re-check runs under the advisory lock below.
            var riderUnitsRequested = items
                .Where(i => tierLookup[i.TierId].Audience == "rider")
                .Sum(i => i.Quantity);
            if (parentEvent.Capacity.HasValue && riderUnitsRequested > 0)
            {
                var eventSold = await _tiers.EventSoldCount(parentEvent.Id, _tenantContext.TenantId);
                if (eventSold + riderUnitsRequested > parentEvent.Capacity.Value)
                {
                    var remaining = Math.Max(0, parentEvent.Capacity.Value - eventSold);
                    return new ApiResponses().BadRequestResult($"Only {remaining} spot(s) left for this event.");
                }
            }

            // Gate-fee enforcement: when this event has a REQUIRED rider gate fee and the
            // cart includes race-class entries, the buyer must also include a rider gate
            // fee — one per rider. Riders aren't identified until the post-payment step, so
            // we bound the gate count to [1, number of class entries] here (a rider may hold
            // several classes, so riders <= entries) and verify the exact per-rider
            // assignment in CompleteRegistration.
            var raceEntryUnits = items.Where(i => tierLookup[i.TierId].Kind == "race_entry").Sum(i => i.Quantity);
            if (raceEntryUnits > 0)
            {
                var hasRequiredRiderGate = eventTiers.Any(t => t.Kind == "gate_fee" && t.Audience == "rider" && t.Required);
                if (hasRequiredRiderGate)
                {
                    var riderGateUnits = items
                        .Where(i => tierLookup[i.TierId].Kind == "gate_fee"
                                 && tierLookup[i.TierId].Audience == "rider"
                                 && tierLookup[i.TierId].Required)
                        .Sum(i => i.Quantity);
                    if (riderGateUnits < 1)
                    {
                        return new ApiResponses().BadRequestResult(
                            "This race requires a rider gate fee. Add one rider gate fee per rider.");
                    }
                    if (riderGateUnits > raceEntryUnits)
                    {
                        return new ApiResponses().BadRequestResult(
                            "You can't have more rider gate fees than race-class entries (a rider can enter several classes).");
                    }
                }
            }

            // Membership gate. Tickets allow guest checkout, so the gate has to
            // route guests to sign-in (no way to verify membership without a user).
            // When the rider opts to bundle a membership into this same checkout
            // (addMembership=true), the gate is skipped — we create the membership
            // purchase below and add its price to the same PaymentIntent.
            var bundleMembership = false;
            if (request.AddMembership && purchaserUserId.HasValue
                && _tenantContext.Tenant.MembershipEnabled
                && _tenantContext.Tenant.MembershipPriceCents > 0)
            {
                var existing = await _memberships.GetActive(purchaserUserId.Value, _tenantContext.TenantId, DateTime.UtcNow);
                bundleMembership = existing is null;
            }
            // Membership is no longer required to buy an entry — tracks that want a
            // "member" relationship for liability fold it into a (waiver-backed) gate fee.
            // Riders may still opt to bundle a membership via addMembership above.

            // Pre-validate extras cart so we can run waiver gating once for the combined order.
            var ticketTenantForExtras = _tenantContext.Tenant;
            var extrasItems = (request.Extras ?? new List<BuyExtrasItem>())
                .Where(i => i.Quantity > 0)
                .GroupBy(i => new { i.ProductId, i.VariantId })
                .Select(g => new BuyExtrasItem
                {
                    ProductId = g.Key.ProductId,
                    VariantId = g.Key.VariantId,
                    Quantity = g.Sum(x => x.Quantity),
                })
                .ToList();
            var extrasLines = new List<(EventExtraProduct Product, EventExtraVariant? Variant, int Quantity,
                                        int UnitAmount, int UnitServiceCharge, int UnitPriceFrozen)>();
            int extrasTotalCents = 0;
            int extrasServiceChargeCents = 0;
            bool extrasNeedWaiver = false;
            if (extrasItems.Count > 0)
            {
                if (!ticketTenantForExtras.ExtrasEnabled)
                {
                    return new ApiResponses().BadRequestResult("Add-ons are not enabled at this track.");
                }

                foreach (var item in extrasItems)
                {
                    var ep = await _extras.GetProduct(item.ProductId, _tenantContext.TenantId);
                    if (ep is null || !ep.IsActive)
                    {
                        return new ApiResponses().BadRequestResult("One of the selected add-ons isn't available.");
                    }
                    var elig = await _extras.GetEligibility(parentEvent.Id, ep.Id);
                    if (elig is null)
                    {
                        return new ApiResponses().BadRequestResult($"\"{ep.Name}\" isn't offered at this event.");
                    }

                    var resolved = await ResolveExtraVariant(ep, item, elig);
                    if (resolved.Error is not null) return new ApiResponses().BadRequestResult(resolved.Error);

                    if (ep.RequiresWaiver) extrasNeedWaiver = true;

                    var unitPriceFrozen = resolved.UnitPriceCents;
                    var serviceChargePerUnit = (int)((long)unitPriceFrozen * ticketTenantForExtras.ServiceChargeBps / 10_000L);
                    var riderPortionPerUnit = (int)((long)serviceChargePerUnit * ep.RiderPaidServiceChargeBps / 10_000L);
                    var unitAmount = unitPriceFrozen + riderPortionPerUnit;
                    extrasLines.Add((ep, resolved.Variant, item.Quantity, unitAmount, serviceChargePerUnit, unitPriceFrozen));
                    extrasTotalCents += unitAmount * item.Quantity;
                    extrasServiceChargeCents += serviceChargePerUnit * item.Quantity;
                }
            }

            // Lesson bike rental pre-validation: an optional bike added while booking a lesson.
            // Its FEE rides this same PaymentIntent (one charge, lesson + bike line items); the
            // refundable deposit becomes a separate manual-capture hold below. The reservation is
            // time-scoped to the lesson's window, so the bike stays free for other lessons and
            // walk-ups outside it. Gift card / coupon apply to the tickets only, not the bike.
            Services.Repositories.Data.BikeShopData.LessonRentableInfo? lessonBike = null;
            DateTime rentalStartsAtUtc = default, rentalEndsAtUtc = default;
            int rentalFrozenRate = 0, rentalQty = 0;
            int rentalFeeCents = 0, rentalDepositCents = 0;
            if (request.LessonRental is not null && request.LessonRental.Quantity > 0)
            {
                if (!purchaserUserId.HasValue)
                    return new ApiResponses().BadRequestResult("Please sign in to add a bike rental to your lesson.");
                if (!_tenantContext.Tenant.BikeShopEnabled)
                    return new ApiResponses().BadRequestResult("Bike rentals aren't enabled at this track.");

                var bike = await _shop.GetLessonRentable(parentEvent.Id, request.LessonRental.VariantId, _tenantContext.TenantId);
                if (bike is null)
                    return new ApiResponses().BadRequestResult("That bike isn't offered with this lesson.");
                if (!bike.IsActive || (bike.PriceCentsOverride ?? bike.DailyRateCents) is null)
                    return new ApiResponses().BadRequestResult("That bike isn't available.");

                rentalStartsAtUtc = DateTime.SpecifyKind(parentEvent.StartsAt, DateTimeKind.Utc);
                rentalEndsAtUtc = DateTime.SpecifyKind(parentEvent.EndsAt, DateTimeKind.Utc);
                rentalQty = Math.Max(1, request.LessonRental.Quantity);

                var free = bike.TrackingKind == "pool"
                    ? await _shop.GetPoolAvailability(bike.VariantId, _tenantContext.TenantId, rentalStartsAtUtc, rentalEndsAtUtc)
                    : (await _shop.GetFreeSerializedUnits(bike.VariantId, _tenantContext.TenantId, rentalStartsAtUtc, rentalEndsAtUtc)).Count;
                if (rentalQty > free)
                    return new ApiResponses().BadRequestResult(
                        free <= 0 ? "That bike is fully booked for this lesson."
                                  : $"Only {free} of that bike available for this lesson.");

                // Shop rentals are all-in priced: no rider service charge, no tax line.
                rentalFrozenRate = (bike.PriceCentsOverride ?? bike.DailyRateCents)!.Value;
                rentalFeeCents = rentalFrozenRate * rentalQty;
                rentalDepositCents = bike.DepositCents * rentalQty;
                lessonBike = bike;
            }

            // Waiver gate: required by the event (rider audience for race entries),
            // or by any selected add-on / bike. Skipped in DeferRegistration mode (unified
            // checkout), which takes payment first and collects the waiver afterward.
            Guid? extrasSignatureId = null;
            if (!request.DeferRegistration && (parentEvent.RequiresRiderWaiver || extrasNeedWaiver))
            {
                if (!purchaserUserId.HasValue)
                {
                    return new ApiResponses().BadRequestResult(
                        parentEvent.RequiresRiderWaiver
                            ? "This event requires a signed waiver — please sign in before purchasing race entries."
                            : "One of the selected add-ons requires a signed waiver — please sign in before purchasing.");
                }
                var activeWaiver = await _waivers.GetActive(_tenantContext.TenantId);
                if (activeWaiver is not null)
                {
                    var sig = await _waivers.GetSignature(purchaserUserId.Value, activeWaiver.Id);
                    if (sig is null)
                    {
                        return new ApiResponses().BadRequestResult("You must sign the current waiver before completing this purchase.");
                    }
                    extrasSignatureId = sig.Id;
                }
            }

            CouponApplication? couponApp = null;
            if (!string.IsNullOrWhiteSpace(request.CouponCode))
            {
                // Cart-wide subtotal for the coupon: tier price * qty summed (no service charge yet).
                var cartSubtotal = items.Sum(i => tierLookup[i.TierId].PriceCents * i.Quantity);
                var validation = await _couponValidator.ValidateAsync(
                    _tenantContext.TenantId, request.CouponCode!, scope: "event_ticket",
                    eventId: parentEvent.Id, subtotalCents: cartSubtotal, userId: purchaserUserId);
                if (validation.error is not null) return new ApiResponses().BadRequestResult(validation.error);
                couponApp = validation.application;
            }

            // ── Season pass benefits ─────────────────────────────────────────────────
            // A holder whose pass covers this event's type gets their entry discounted (100% = in
            // free). The discount is an entitlement OF A PASS, so the number of tickets it can
            // cover is the number of covering passes the buyer holds — one pass discounts one
            // entry no matter how many are in the cart, and a parent holding three discounts three.
            //
            // Guests get nothing: a pass belongs to an account, and there's no way to know a guest
            // holds one. Repo-side the grants are already filtered to passes that are paid,
            // registered, and valid on the event's date (window + day-of-week + credits), so a
            // rider can't claim free entry with an unregistered or out-of-season pass.
            var passBenefitByTicketIndex = new Dictionary<int, SeasonPassBenefitGrant>();
            if (purchaserUserId.HasValue)
            {
                var grants = await _seasonPasses.ListActiveBenefitGrantsForUser(
                    purchaserUserId.Value, _tenantContext.TenantId,
                    benefitType: "event", scopeId: parentEvent.EventTypeId, onDateUtc: parentEvent.StartsAt);
                if (grants.Count > 0)
                {
                    // Rider-audience tiers only. A pass admits a rider, so discounting a
                    // spectator's gate fee would hand a rider's entitlement to their guests.
                    var eligible = new List<(int Index, int PriceCents)>();
                    var idx = 0;
                    foreach (var item in items)
                    {
                        var t = tierLookup[item.TierId];
                        var riderAudience = t.Kind == "race_entry" || (t.Kind == "gate_fee" && t.Audience == "rider");
                        for (var q = 0; q < item.Quantity; q++, idx++)
                        {
                            if (riderAudience && t.PriceCents > 0) eligible.Add((idx, t.PriceCents));
                        }
                    }

                    // Most expensive first, best benefit first: when the cart has more eligible
                    // entries than passes, the buyer should save the most the passes allow rather
                    // than have it land on whichever ticket happened to be added first.
                    var ranked = grants
                        .OrderByDescending(g => g.Benefit.DiscountFor(eligible.Count > 0 ? eligible.Max(e => e.PriceCents) : 0))
                        .ToList();
                    foreach (var (ticketIndex, _) in eligible.OrderByDescending(e => e.PriceCents))
                    {
                        if (passBenefitByTicketIndex.Count >= ranked.Count) break;
                        passBenefitByTicketIndex[ticketIndex] = ranked[passBenefitByTicketIndex.Count];
                    }
                }
            }

            // Create one purchase row per unit. Each gets its own redemption token (QR).
            //
            // Coupon distribution: the validator returned a single discount for the whole cart
            // subtotal. We split it pro-rata across line items by sticker price so each ledger
            // row reflects what was actually discounted on it. Last unit absorbs rounding so the
            // sum exactly matches the validator's cap.
            var ticketTenant = _tenantContext.Tenant;
            var admissionTax = await LoadAdmissionTaxConfig(_tenantContext.TenantId);
            var couponSubtotalDenom = couponApp is null ? 0 : items.Sum(i => tierLookup[i.TierId].PriceCents * i.Quantity);
            var couponRemaining = couponApp?.DiscountCents ?? 0;
            var totalUnitsRemaining = totalUnits;

            var createdTickets = new List<(EventTicketPurchase purchase, Services.Repositories.Data.PaymentData.EventTicketTier tier, int unitAmountCents, int unitServiceChargeCents, int couponDiscountCents)>();

            // Serialize the capacity recheck + row inserts per event so two concurrent
            // last-spot buyers can't both pass the check and oversell, and a rider can't
            // double-enter a race class via a near-simultaneous request (review item #4).
            // The lock is released right after the inserts (capacity is committed by the
            // pending rows) and before the Stripe call so we never hold it across the network.
            await using var capacityLock = await _db.AcquireAdvisoryLock($"event-capacity:{parentEvent.Id}");

            // Authoritative re-check under the lock — the early loop above is just a fast-fail.
            if (parentEvent.Capacity.HasValue && riderUnitsRequested > 0)
            {
                var eventSoldNow = await _tiers.EventSoldCount(parentEvent.Id, _tenantContext.TenantId);
                if (eventSoldNow + riderUnitsRequested > parentEvent.Capacity.Value)
                {
                    var remainingNow = Math.Max(0, parentEvent.Capacity.Value - eventSoldNow);
                    return new ApiResponses().BadRequestResult($"Only {remainingNow} spot(s) left for this event.");
                }
            }
            foreach (var item in items)
            {
                var lockTier = tierLookup[item.TierId];
                var lockCap = await EffectiveTierCap(lockTier);
                if (lockCap is int lockCapValue)
                {
                    var soldNow = await _tiers.SoldCount(lockTier.Id);
                    if (soldNow + item.Quantity > lockCapValue)
                    {
                        var remainingNow = Math.Max(0, lockCapValue - soldNow);
                        return new ApiResponses().BadRequestResult(
                            $"Not enough '{lockTier.Name}' left ({remainingNow} remaining, you asked for {item.Quantity}).");
                    }
                }
                if (lockTier.Kind == "race_entry" && !request.DeferRegistration)
                {
                    var alreadyNow = await _ticketPurchases.HasActiveRaceEntry(
                        _tenantContext.TenantId, lockTier.Id, purchaserUserId, purchaserEmail);
                    if (alreadyNow)
                    {
                        return new ApiResponses().BadRequestResult(
                            $"You're already entered in '{lockTier.Name}' — riders can only enter each class once.");
                    }
                }
            }

            // Burn ride credits UP FRONT, before any purchase row exists (gift-card pattern:
            // debit at checkout, hand back on failure). A credits grant only ever reaches here
            // 100%-off with credits_remaining > 0 (repo-side filter), but a concurrent purchase
            // or gate scan can race the last credit away between the grant fetch and now — in
            // that case compensate any burns already made in this request and abort cleanly
            // while nothing has been created or charged.
            var burnedPassIds = new List<Guid>();
            foreach (var creditGrant in passBenefitByTicketIndex.Values.Where(g => g.ProductKind == "credits"))
            {
                var burned = await _seasonPasses.TryDecrementCredits(creditGrant.PassPurchaseId, _tenantContext.TenantId);
                if (burned == 0)
                {
                    foreach (var passId in burnedPassIds)
                    {
                        await _seasonPasses.IncrementCredits(passId, _tenantContext.TenantId);
                    }
                    return new ApiResponses().BadRequestResult(
                        "A ride credit on your season pass was just used elsewhere, so this order was not charged. Review the updated price and try again.");
                }
                burnedPassIds.Add(creditGrant.PassPurchaseId);
            }

            var unitIndex = -1;
            foreach (var item in items)
            {
                var tier = tierLookup[item.TierId];
                for (int q = 0; q < item.Quantity; q++)
                {
                    unitIndex++;
                    // Party pricing: the base covers the first PartySizeIncluded riders and any
                    // beyond that are charged PartyPriceCents. Defaults give plain per-person
                    // pricing, so ordinary tiers are unaffected.
                    var unitPrice = Services.Pricing.PartyPricing.UnitPriceCents(tier, q);
                    // Pass benefit before the coupon: it's an entitlement the holder already paid
                    // for, so it comes off the sticker price, and a coupon then discounts what's
                    // actually left to pay rather than being swallowed by a free entry.
                    var passGrant = passBenefitByTicketIndex.GetValueOrDefault(unitIndex);
                    if (passGrant is not null)
                    {
                        unitPrice -= passGrant.Benefit.DiscountFor(unitPrice);
                    }

                    int unitCouponDiscount = 0;
                    if (couponApp is not null && couponSubtotalDenom > 0)
                    {
                        // Proportional split; last remaining unit absorbs the rounding remainder.
                        unitCouponDiscount = totalUnitsRemaining == 1
                            ? couponRemaining
                            : (int)((long)couponApp.DiscountCents * tier.PriceCents / couponSubtotalDenom);
                        if (unitCouponDiscount > unitPrice) unitCouponDiscount = unitPrice;
                        unitPrice -= unitCouponDiscount;
                        couponRemaining -= unitCouponDiscount;
                        totalUnitsRemaining--;
                    }

                    var (unitPreTax, unitServiceCharge) = ComputeWithServiceCharge(
                        unitPrice, quantity: 1, ticketTenant.ServiceChargeBps, tier.RiderPaidServiceChargeBps);

                    // Admission tax on the post-discount base (+ rider fee when taxable). amount_cents
                    // is stored tax-inclusive: on-top tax grows it; inclusive tax leaves it unchanged.
                    var tax = AdmissionTax.Compute(unitPrice, unitPreTax - unitPrice, admissionTax);
                    var unitAmount = tax.AmountToChargeCents;

                    var purchase = new EventTicketPurchase
                    {
                        TenantId = _tenantContext.TenantId,
                        TierId = tier.Id,
                        PurchaserUserId = purchaserUserId,
                        AmountCents = unitAmount,
                        ServiceChargeCents = unitServiceCharge,
                        TaxCents = tax.TaxCents,
                        TaxRateBps = admissionTax.RateBps,
                        TaxInclusive = admissionTax.PricesIncludeTax,
                        // The funding pass for a credit-covered ticket (burn already done above),
                        // so a refund or failed payment knows which pass gets the ride back.
                        AppliedSeasonPassPurchaseId = passGrant?.ProductKind == "credits" ? passGrant.PassPurchaseId : null,
                        PaymentMethod = unitAmount == 0 ? "comped" : "stripe",
                        Status = "pending",
                        PurchaserEmail = purchaserEmail,
                        PurchaserName = purchaserName,
                        // Deferred (unified checkout) tickets collect rider + waiver after
                        // payment. A race entry or a rider gate fee always needs a rider
                        // (name + any rider waiver). A spectator gate fee only needs the
                        // step when the event requires a spectator waiver — otherwise
                        // there's nothing to collect, so it's complete.
                        RegistrationComplete = !(request.DeferRegistration && (
                            tier.Kind == "race_entry"
                            || (tier.Kind == "gate_fee" && tier.Audience == "rider")
                            || (tier.Kind == "gate_fee" && tier.Audience == "spectator" && parentEvent.RequiresSpectatorWaiver))),
                    };
                    var created = await _ticketPurchases.Create(purchase);
                    purchase.Id = created.Id;
                    purchase.RedemptionToken = created.RedemptionToken;
                    createdTickets.Add((purchase, tier, unitAmount, unitServiceCharge, unitCouponDiscount));
                }
            }

            // Pending rows now hold the capacity; release the per-event lock before the
            // (network) Stripe call. Disposal is idempotent with the `await using` above.
            await capacityLock.DisposeAsync();

            // Record one coupon_redemption row per ticket that received a discount. The
            // unique constraint on (source_kind, source_id) means a retry can't double-count.
            if (couponApp is not null)
            {
                foreach (var t in createdTickets.Where(t => t.couponDiscountCents > 0))
                {
                    await _coupons.RecordRedemption(new CouponRedemption
                    {
                        CouponId = couponApp.Coupon.Id,
                        TenantId = _tenantContext.TenantId,
                        UserId = purchaserUserId,
                        SourceKind = "event_ticket",
                        SourceId = t.purchase.Id,
                        DiscountCents = t.couponDiscountCents,
                    });
                }
            }

            var totalAmountCents = createdTickets.Sum(t => t.unitAmountCents);
            var totalServiceChargeCents = createdTickets.Sum(t => t.unitServiceChargeCents);

            // Gift card: applies AFTER all discounts as a payment instrument. Distribute
            // the applied chunk pro-rata across line items (last absorbs rounding) so per-
            // ticket gift_card_redemption rows are accurate, then reduce the Stripe charge.
            GiftCardApplication? gcApp = null;
            int gcRemaining = 0;
            if (!string.IsNullOrWhiteSpace(request.GiftCardCode) && totalAmountCents > 0)
            {
                var gcCheck = await _giftCardValidator.ResolveAsync(_tenantContext.TenantId,
                    request.GiftCardCode!, totalAmountCents);
                if (gcCheck.error is not null) return new ApiResponses().BadRequestResult(gcCheck.error);
                gcApp = gcCheck.application;
                gcRemaining = gcApp!.AmountToApplyCents;
                // Debit the card up front with an atomic conditional decrement so two concurrent
                // checkouts on the same card can't both spend it; the loser gets a clean error.
                if (!await _giftCards.ApplyToBalance(gcApp.Card.Id, gcApp.AmountToApplyCents))
                    return new ApiResponses().BadRequestResult(
                        "That gift card's balance just changed. Please re-apply it and try again.");
            }
            var stripeChargeCents = totalAmountCents - (gcApp?.AmountToApplyCents ?? 0);

            var redemptions = createdTickets.Select(t => new TicketRedemption
            {
                PurchaseId = t.purchase.Id,
                RedemptionToken = t.purchase.RedemptionToken,
                TierName = t.tier.Name,
                AmountCents = t.unitAmountCents,
            }).ToList();
            var first = createdTickets[0].purchase;

            // Distribute gift-card chunks per ticket and record redemption rows. Done
            // before the free-cart path so a card that fully covers the cart still
            // gets per-ticket redemption rows recorded.
            var perTicketGiftCard = new Dictionary<Guid, int>();
            if (gcApp is not null)
            {
                int unitsLeft = createdTickets.Count;
                foreach (var t in createdTickets)
                {
                    var share = unitsLeft == 1
                        ? gcRemaining
                        : (int)((long)gcApp.AmountToApplyCents * t.unitAmountCents / Math.Max(1, totalAmountCents));
                    if (share > t.unitAmountCents) share = t.unitAmountCents;
                    perTicketGiftCard[t.purchase.Id] = share;
                    gcRemaining -= share;
                    unitsLeft--;
                }
                foreach (var t in createdTickets)
                {
                    var amt = perTicketGiftCard[t.purchase.Id];
                    if (amt <= 0) continue;
                    await _giftCards.RecordRedemption(new GiftCardRedemption
                    {
                        GiftCardId = gcApp.Card.Id,
                        TenantId = _tenantContext.TenantId,
                        UserId = purchaserUserId,
                        SourceKind = "event_ticket",
                        SourceId = t.purchase.Id,
                        AmountCents = amt,
                    });
                }
                // Balance was already debited up front (atomic conditional decrement above).
            }

            // Persist extras rows (one per unit so each gets its own QR). They share
            // the tickets' PaymentIntent so the webhook flips them all together.
            var extraPurchaseIds = new List<Guid>();
            foreach (var line in extrasLines)
            {
                for (int q = 0; q < line.Quantity; q++)
                {
                    var ep = new EventExtraPurchase
                    {
                        TenantId = _tenantContext.TenantId,
                        EventId = parentEvent.Id,
                        ProductId = line.Product.Id,
                        PurchaserUserId = purchaserUserId,
                        PurchaserEmail = purchaserEmail,
                        PurchaserName = purchaserName,
                        WaiverSignatureId = line.Product.RequiresWaiver ? extrasSignatureId : null,
                        Quantity = 1,
                        UnitPriceCentsFrozen = line.UnitPriceFrozen,
                        AmountCents = line.UnitAmount,
                        ServiceChargeCents = line.UnitServiceCharge,
                        Status = "pending",
                        PaymentMethod = "stripe",
                        VariantId = line.Variant?.Id,
                        SizeAtPurchase = line.Variant?.Size,
                        ColorAtPurchase = line.Variant?.Color,
                        GenderAtPurchase = line.Variant?.Gender,
                    };
                    var created = await _extras.CreatePurchase(ep);
                    extraPurchaseIds.Add(created.Id);
                }
            }

            // Create the lesson bike rental row (pending, shop_rental) so its fee rides the
            // combined PaymentIntent. The finalizer resolves it by the fee PI id and flips it to
            // paid with the tickets; its window-scoped reservation holds capacity from creation.
            Guid? lessonShopRentalId = null;
            if (lessonBike is not null)
            {
                var rentalLines = new List<Services.Repositories.Data.BikeShopData.ShopRentalLine>();
                var bikeLabel = string.Join(" / ", new[] { lessonBike.Size, lessonBike.Color, lessonBike.Gender }
                    .Where(x => !string.IsNullOrWhiteSpace(x)));
                if (lessonBike.TrackingKind == "serialized")
                {
                    var units = await _shop.GetFreeSerializedUnits(
                        lessonBike.VariantId, _tenantContext.TenantId, rentalStartsAtUtc, rentalEndsAtUtc);
                    if (units.Count < rentalQty)
                        return new ApiResponses().BadRequestResult("That bike was just taken for this lesson. Please retry.");
                    rentalLines.AddRange(units.Take(rentalQty).Select(u => new Services.Repositories.Data.BikeShopData.ShopRentalLine
                    {
                        VariantId = lessonBike.VariantId,
                        ItemId = u.Id,
                        Quantity = 1,
                        NameSnapshot = lessonBike.ProductName,
                        VariantLabel = string.IsNullOrEmpty(bikeLabel) ? null : bikeLabel,
                        DailyRateCentsFrozen = rentalFrozenRate,
                        DepositCentsFrozen = lessonBike.DepositCents,
                        LineAmountCents = rentalFrozenRate,
                    }));
                }
                else
                {
                    rentalLines.Add(new Services.Repositories.Data.BikeShopData.ShopRentalLine
                    {
                        VariantId = lessonBike.VariantId,
                        Quantity = rentalQty,
                        NameSnapshot = lessonBike.ProductName,
                        VariantLabel = string.IsNullOrEmpty(bikeLabel) ? null : bikeLabel,
                        DailyRateCentsFrozen = rentalFrozenRate,
                        DepositCentsFrozen = lessonBike.DepositCents,
                        LineAmountCents = rentalFeeCents,
                    });
                }
                var (rid, _) = await _shop.CreateRental(new Services.Repositories.Data.BikeShopData.ShopRental
                {
                    TenantId = _tenantContext.TenantId,
                    RenterUserId = purchaserUserId,
                    RenterName = purchaserName,
                    RenterEmail = purchaserEmail,
                    WaiverSignatureId = extrasSignatureId,
                    StartsAt = rentalStartsAtUtc,
                    EndsAt = rentalEndsAtUtc,
                    Status = "pending",
                    AmountCents = rentalFeeCents,
                    TaxCents = 0,
                    TotalCents = rentalFeeCents,
                    DepositCents = rentalDepositCents,
                    PaymentMethod = "stripe",
                    EventId = parentEvent.Id,
                }, rentalLines);
                lessonShopRentalId = rid;
            }

            // Build the membership purchase up front (when bundling) so its price
            // counts toward the combined PI total. The row gets stamped with the PI
            // id below alongside ticket / extras rows so the webhook flips it on
            // payment_intent.succeeded.
            Guid? bundledMembershipPurchaseId = null;
            int membershipChargeCents = 0;
            int membershipServiceChargeCents = 0;
            if (bundleMembership && purchaserUserId.HasValue)
            {
                var tenant = _tenantContext.Tenant;
                var nowUtc = DateTime.UtcNow;
                DateTime? validTo = tenant.MembershipDurationKind == "yearly" ? nowUtc.AddDays(365) : (DateTime?)null;
                var membershipServiceCharge = (int)((long)tenant.MembershipPriceCents * tenant.ServiceChargeBps / 10_000L);
                membershipServiceChargeCents = membershipServiceCharge;
                var membership = new Services.Repositories.Data.MembershipData.MembershipPurchase
                {
                    TenantId = tenant.Id,
                    UserId = purchaserUserId.Value,
                    NameAtPurchase = tenant.MembershipName,
                    PriceCents = tenant.MembershipPriceCents,
                    DurationKind = tenant.MembershipDurationKind,
                    ValidFromUtc = nowUtc,
                    ValidToUtc = validTo,
                    AmountCents = tenant.MembershipPriceCents,
                    ServiceChargeCents = membershipServiceCharge,
                    Status = "pending",
                    PaymentMethod = "stripe",
                };
                bundledMembershipPurchaseId = await _memberships.Create(membership);
                membershipChargeCents = tenant.MembershipPriceCents;
            }

            var combinedStripeChargeCents = stripeChargeCents + extrasTotalCents + membershipChargeCents + rentalFeeCents;

            // ── Store credit (Script0195): a logged-in rider burns their balance as the LAST
            // tender, after discounts + gift card. Debited now through a tender row anchoring
            // the whole checkout; the finalizer hands it back if the payment fails or the cart
            // is abandoned. A raced balance simply means no credit applies.
            Guid? creditTenderId = null;
            var creditAppliedCents = 0;
            if (request.UseStoreCredit && purchaserUserId.HasValue && combinedStripeChargeCents > 0)
            {
                var creditAccount = await _credit.GetAccountForUser(_tenantContext.TenantId, purchaserUserId.Value);
                if (creditAccount is not null && creditAccount.BalanceCents > 0)
                {
                    var toApply = Math.Min(creditAccount.BalanceCents, combinedStripeChargeCents);
                    creditTenderId = await _credit.TryCreateCheckoutTender(
                        _tenantContext.TenantId, creditAccount.Id, toApply, "event_checkout");
                    if (creditTenderId is not null) creditAppliedCents = toApply;
                }
            }
            combinedStripeChargeCents -= creditAppliedCents;

            // Free-cart fast path: gift card fully covered, store credit covering it all, or a
            // cart that priced to zero, AND no remaining add-on charges.
            if (combinedStripeChargeCents == 0)
            {
                foreach (var t in createdTickets)
                {
                    await _ticketPurchases.UpdateStatus(t.purchase.Id, "paid");
                    // A gift card fully covered this ticket (gcApp set) is NOT a $0 sale: the buyer
                    // paid real money for the card, so the tenant is owed the value. Store-credit
                    // coverage books the same shape (value owed), then the tender's balancing entry
                    // below nets the credit-funded part back out. Anything else is a genuine $0 sale.
                    if (gcApp is not null && perTicketGiftCard.TryGetValue(t.purchase.Id, out var gcAmt) && gcAmt > 0)
                        await InsertGiftCardCoveredLedger(_tenantContext.TenantId, "event_ticket",
                            t.purchase.Id, t.unitAmountCents, t.unitServiceChargeCents);
                    else if (creditAppliedCents > 0)
                        await InsertGiftCardCoveredLedger(_tenantContext.TenantId, "event_ticket",
                            t.purchase.Id, t.unitAmountCents, t.unitServiceChargeCents);
                    else
                        await InsertZeroLedger(_tenantContext.TenantId, "event_ticket", t.purchase.Id);
                }
                if (creditTenderId is not null)
                    await BookCreditTenderEntryNow(creditTenderId.Value, creditAppliedCents);
                foreach (var exId in extraPurchaseIds)
                {
                    await _extras.UpdateStatus(exId, "paid");
                }

                // A free lesson can still come with a bike carrying a refundable deposit: the fee is
                // $0 (no main charge) but the deposit is still held. Mark the rental paid and, if it
                // has a deposit, place the manual-capture hold on its own PI. The hold is the only
                // Stripe interaction in this otherwise-free order.
                string? freeDepositHoldSecret = null;
                if (lessonShopRentalId.HasValue)
                {
                    if (await _shop.TryMarkRentalPaid(lessonShopRentalId.Value, _tenantContext.TenantId))
                    {
                        var rOrder = await _shop.NextOrderNumber(_tenantContext.TenantId);
                        await _shop.SetRentalOrderNumber(lessonShopRentalId.Value, rOrder);
                    }
                    if (rentalDepositCents > 0)
                    {
                        ChargePlan holdPlan;
                        try { holdPlan = _chargeRouter.Plan(_tenantContext.Tenant, 0, rentalDepositCents); }
                        catch (InvalidOperationException ex) { return new ApiResponses().BadRequestResult(ex.Message); }
                        if (holdPlan.IsDirect)
                            await _shop.MarkRentalDirectCharge(lessonShopRentalId.Value, _tenantContext.TenantId, holdPlan.ConnectedAccountId!);
                        var holdMeta = new Dictionary<string, string>
                        {
                            ["tenant_id"] = _tenantContext.TenantId.ToString(),
                            ["shop_rental_id"] = lessonShopRentalId.Value.ToString(),
                            ["sale_kind"] = "shop_rental_deposit_hold",
                        };
                        try
                        {
                            var hold = await _payments.CreateHoldPaymentIntentAsync(
                                rentalDepositCents, "usd", holdMeta, purchaserEmail, holdPlan.ConnectedAccountId, ct);
                            await _shop.SetRentalDepositIntent(lessonShopRentalId.Value, hold.IntentId);
                            freeDepositHoldSecret = hold.ClientSecret;
                        }
                        catch (InvalidOperationException ex) { return new ApiResponses().BadRequestResult(ex.Message); }
                    }
                }

                // A free entry is still an entry: it needs the same confirmation + QR as a paid one.
                // This path never reaches the Stripe webhook (there's no PaymentIntent), so the email
                // has to be sent here or the rider gets nothing at all. The ticket email already lists
                // any add-ons on the order; a cart that somehow has only add-ons confirms on its own.
                if (createdTickets.Count > 0)
                    await _orderConfirmations.SendForTickets(_tenantContext.TenantId,
                        createdTickets.Select(t => t.purchase.Id).ToList());
                else if (extraPurchaseIds.Count > 0)
                    await _orderConfirmations.SendForExtras(_tenantContext.TenantId, extraPurchaseIds);

                return new ApiResponses().OkResult(new CreatePurchaseResponse
                {
                    PurchaseId = first.Id,
                    RedemptionToken = first.RedemptionToken,
                    Tickets = redemptions,
                    ClientSecret = string.Empty,
                    AmountCents = 0,
                    RiderServiceChargeCents = 0,
                    CreditAppliedCents = creditAppliedCents,
                    DepositHoldClientSecret = freeDepositHoldSecret,
                    RentalFeeCents = rentalFeeCents,
                    RentalDepositCents = rentalDepositCents,
                });
            }

            // Single PaymentIntent for the whole cart so the rider sees one Stripe charge.
            var metadata = new Dictionary<string, string>
            {
                ["tenant_id"] = _tenantContext.TenantId.ToString(),
                ["event_id"] = parentEvent.Id.ToString(),
                ["ticket_count"] = createdTickets.Count.ToString(),
                ["ticket_purchase_ids"] = string.Join(",", createdTickets.Select(t => t.purchase.Id)),
            };
            if (purchaserUserId.HasValue)
            {
                metadata["user_id"] = purchaserUserId.Value.ToString();
            }

            if (gcApp is not null)
            {
                metadata["gift_card_applied_cents"] = gcApp.AmountToApplyCents.ToString();
                metadata["gift_card_id"] = gcApp.Card.Id.ToString();
            }

            if (extraPurchaseIds.Count > 0)
            {
                metadata["extra_purchase_ids"] = string.Join(",", extraPurchaseIds);
                metadata["extras_total_cents"] = extrasTotalCents.ToString();
            }

            if (bundledMembershipPurchaseId.HasValue)
            {
                metadata["membership_purchase_id"] = bundledMembershipPurchaseId.Value.ToString();
                metadata["membership_charge_cents"] = membershipChargeCents.ToString();
            }

            if (lessonShopRentalId.HasValue)
            {
                metadata["shop_rental_id"] = lessonShopRentalId.Value.ToString();
                metadata["rental_fee_cents"] = rentalFeeCents.ToString();
            }

            // Direct-charge tenants charge on their own connected account; our service fee rides as
            // the Stripe application fee (that is how RidePass gets paid). serviceFeeCents is the total
            // RidePass cut for this cart (ticket + extras + bundled-membership + bike-fee service charges;
            // never the refundable deposit, which is a separate hold carrying no application fee).
            var serviceFeeCents = (long)totalServiceChargeCents + extrasServiceChargeCents
                + membershipServiceChargeCents;

            PaymentIntentCreated intent;
            ChargePlan chargePlan;
            try
            {
                chargePlan = _chargeRouter.Plan(_tenantContext.Tenant, serviceFeeCents, combinedStripeChargeCents);
                intent = await _payments.CreatePaymentIntentAsync(
                    amountCents: combinedStripeChargeCents,
                    currency: "usd",
                    metadata: metadata,
                    receiptEmail: purchaserEmail,
                    connectedAccountId: chargePlan.ConnectedAccountId,
                    applicationFeeCents: chargePlan.ApplicationFeeCents,
                    ct: ct);
            }
            catch (InvalidOperationException ex)
            {
                if (creditTenderId is not null)
                    await _credit.ReverseRedeem(_tenantContext.TenantId, "credit_tender", creditTenderId.Value, "payment could not start");
                return new ApiResponses().BadRequestResult(ex.Message);
            }
            // Key the tender by PI so the webhook can book the balancing entry (or hand the
            // credit back on failure/abandon).
            if (creditTenderId is not null)
                await _credit.SetCheckoutTenderPaymentIntent(creditTenderId.Value, _tenantContext.TenantId, intent.IntentId);

            // Every ticket row points at the same PI so the webhook handler can find them all.
            // For direct charges, snapshot the connected account on each row so refunds/finalization
            // act on the right account regardless of any later tenant mode change.
            foreach (var t in createdTickets)
            {
                await _ticketPurchases.SetStripePaymentIntentId(t.purchase.Id, intent.IntentId);
                if (chargePlan.IsDirect)
                {
                    await _ticketPurchases.MarkDirectCharge(t.purchase.Id, _tenantContext.TenantId, chargePlan.ConnectedAccountId!);
                }
            }
            foreach (var exId in extraPurchaseIds)
            {
                await _extras.SetPaymentIntentId(exId, intent.IntentId);
                if (chargePlan.IsDirect)
                {
                    await _extras.MarkDirectCharge(exId, _tenantContext.TenantId, chargePlan.ConnectedAccountId!);
                }
            }
            if (bundledMembershipPurchaseId.HasValue)
            {
                await _memberships.SetStripePaymentIntentId(bundledMembershipPurchaseId.Value, intent.IntentId);
                if (chargePlan.IsDirect)
                {
                    await _memberships.MarkDirectCharge(bundledMembershipPurchaseId.Value, _tenantContext.TenantId, chargePlan.ConnectedAccountId!);
                }
            }

            // Point the bike rental at the same PI so the finalizer flips it to paid with the tickets,
            // then place the refundable deposit as a SEPARATE manual-capture hold on the same account
            // (no application fee — RidePass takes no cut of a deposit). The client confirms the main
            // charge first, then this hold, with the same card.
            string? depositHoldClientSecret = null;
            if (lessonShopRentalId.HasValue)
            {
                await _shop.SetRentalPaymentIntent(lessonShopRentalId.Value, intent.IntentId);
                if (chargePlan.IsDirect)
                {
                    await _shop.MarkRentalDirectCharge(lessonShopRentalId.Value, _tenantContext.TenantId, chargePlan.ConnectedAccountId!);
                }
                if (rentalDepositCents > 0)
                {
                    var holdMeta = new Dictionary<string, string>
                    {
                        ["tenant_id"] = _tenantContext.TenantId.ToString(),
                        ["shop_rental_id"] = lessonShopRentalId.Value.ToString(),
                        ["sale_kind"] = "shop_rental_deposit_hold",
                    };
                    if (purchaserUserId.HasValue) holdMeta["user_id"] = purchaserUserId.Value.ToString();
                    try
                    {
                        var hold = await _payments.CreateHoldPaymentIntentAsync(
                            rentalDepositCents, "usd", holdMeta, purchaserEmail, chargePlan.ConnectedAccountId, ct);
                        await _shop.SetRentalDepositIntent(lessonShopRentalId.Value, hold.IntentId);
                        depositHoldClientSecret = hold.ClientSecret;
                    }
                    catch (InvalidOperationException ex)
                    {
                        // The main lesson+fee PI is created but unconfirmed; failing here means the client
                        // never confirms it and it expires, and the reconciler fails the pending rows.
                        return new ApiResponses().BadRequestResult(ex.Message);
                    }
                }
            }

            return new ApiResponses().OkResult(new CreatePurchaseResponse
            {
                PurchaseId = first.Id,
                RedemptionToken = first.RedemptionToken,
                Tickets = redemptions,
                ClientSecret = intent.ClientSecret,
                AmountCents = combinedStripeChargeCents,
                RiderServiceChargeCents = totalServiceChargeCents + extrasServiceChargeCents,
                TaxCents = createdTickets.Sum(t => t.purchase.TaxCents),
                GiftCardAppliedCents = gcApp?.AmountToApplyCents ?? 0,
                CreditAppliedCents = creditAppliedCents,
                DepositHoldClientSecret = depositHoldClientSecret,
                RentalFeeCents = rentalFeeCents,
                RentalDepositCents = rentalDepositCents,
            });
        }

        // Unified-checkout post-payment step: attach each paid ticket's rider identity +
        // signed waiver. Guest-accessible (the ticket ids come from the checkout response).
        // A ticket whose event requires a waiver for its audience (rider vs spectator) must
        // include a signature, otherwise it's rejected and stays registration-incomplete
        // (gate check-in flags it; a follow-up email nudges the purchaser to finish).
        [AllowAnonymous]
        [HttpPost("EventTicket/CompleteRegistration")]
        public async Task<IActionResult> CompleteTicketRegistration([FromBody] CompleteTicketRegistrationRequest request)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var tenantId = _tenantContext.TenantId;

            Guid? activeWaiverId = null;
            var activeFetched = false;
            var completed = 0;
            // All ticket ids in this submission, excluded from the cross-order uniqueness
            // check so a rider doesn't conflict with their own (or a re-submitted) entry.
            var requestTicketIds = request.Registrants
                .SelectMany(r => r.Tickets ?? new List<RegistrantTicketItem>())
                .Select(t => t.TicketId).ToList();
            // Cache of an event's tiers, to resolve which tiers form a race "class" (a price
            // ladder shares one class across all its steps).
            var eventTierCache = new Dictionary<Guid, List<Services.Repositories.Data.PaymentData.EventTicketTier>>();

            foreach (var reg in request.Registrants)
            {
                if (string.IsNullOrWhiteSpace(reg.FirstName) || string.IsNullOrWhiteSpace(reg.LastName))
                {
                    return new ApiResponses().BadRequestResult("Each rider needs a first and last name.");
                }
                if (reg.Tickets is null || reg.Tickets.Count == 0)
                {
                    return new ApiResponses().BadRequestResult($"{reg.FirstName} needs at least one ticket assigned.");
                }

                // Load + validate every ticket this registrant covers, and decide whether
                // any of them needs a waiver (rider waiver for race entries / rider gate
                // fees; spectator waiver for spectator gate fees).
                var loaded = new List<(EventTicketPurchase ticket, Services.Repositories.Data.PaymentData.EventTicketTier tier, bool isRace, bool needsWaiver, Guid? waiverId, string? raceNumber)>();
                var seenRaceClasses = new HashSet<string>();
                foreach (var ti in reg.Tickets)
                {
                    var ticket = await _ticketPurchases.GetById(ti.TicketId, tenantId);
                    if (ticket is null) return new ApiResponses().NotFoundResult("Ticket not found.");
                    // Registration is identity + waiver capture, not a money step, and the
                    // webhook that flips 'pending' → 'paid' lands a few seconds after the
                    // client-side payment confirmation. So accept a still-'pending' ticket
                    // (the rider just paid); only reject genuinely dead ones. Check-in still
                    // honors paid/redeemed only, so a never-paid ticket can't sneak in.
                    if (ticket.Status is "cancelled" or "refunded" or "failed")
                    {
                        return new ApiResponses().BadRequestResult("That ticket is no longer valid.");
                    }
                    // A checked-in ticket's rider identity + signed waiver are a locked legal record;
                    // anyone holding the ticket GUID must not be able to overwrite them post-admission.
                    if (ticket.Status == "redeemed")
                    {
                        return new ApiResponses().BadRequestResult("That ticket is already checked in and can't be re-registered.");
                    }
                    var tier = await _tiers.GetById(ticket.TierId, tenantId);
                    var ev = tier is null ? null : await _events.GetById(tier.EventId, tenantId);
                    if (tier is null || ev is null) return new ApiResponses().BadRequestResult("Ticket is missing its event.");

                    var isRace = tier.Kind == "race_entry";
                    // A class spans all steps of a price ladder, so key uniqueness on the
                    // ladder group (falling back to the tier id for a standalone class).
                    var classKey = tier.LadderGroup ?? tier.Id.ToString();
                    if (isRace && !seenRaceClasses.Add(classKey))
                    {
                        return new ApiResponses().BadRequestResult($"A rider can only enter '{tier.Name}' once.");
                    }

                    // Cross-order per-rider uniqueness: the same rider (name + birthdate) and
                    // their race number must each be unique within the class.
                    if (isRace)
                    {
                        if (!eventTierCache.TryGetValue(tier.EventId, out var evTiers))
                        {
                            evTiers = await _tiers.GetForEvent(tier.EventId, tenantId, activeOnly: false);
                            eventTierCache[tier.EventId] = evTiers;
                        }
                        var classTierIds = tier.LadderGroup is null
                            ? new List<Guid> { tier.Id }
                            : evTiers.Where(t => t.LadderGroup == tier.LadderGroup).Select(t => t.Id).ToList();
                        var conflict = await _ticketPurchases.FindRaceClassConflict(
                            tenantId, classTierIds, reg.FirstName!.Trim(), reg.LastName!.Trim(),
                            reg.Birthdate, ti.RaceNumber?.Trim(), requestTicketIds);
                        if (conflict == "person")
                        {
                            return new ApiResponses().BadRequestResult(
                                $"{reg.FirstName} {reg.LastName} is already entered in '{tier.Name}' — a rider can only enter a class once.");
                        }
                        if (conflict == "number")
                        {
                            return new ApiResponses().BadRequestResult(
                                $"Race number {ti.RaceNumber} is already taken in '{tier.Name}'.");
                        }
                    }

                    // Rider audiences (race entry + rider gate fee) use the rider waiver;
                    // a spectator gate fee uses the spectator waiver.
                    var isRiderAudience = isRace || (tier.Kind == "gate_fee" && tier.Audience == "rider");
                    var needsWaiver = isRiderAudience ? ev.RequiresRiderWaiver : ev.RequiresSpectatorWaiver;
                    var waiverId = isRiderAudience ? ev.RacerWaiverId : ev.SpectatorWaiverId;
                    loaded.Add((ticket, tier, isRace, needsWaiver, waiverId, ti.RaceNumber));
                }

                if (loaded.Any(x => x.needsWaiver) && !IsValidSignatureDataUrl(reg.WaiverSignatureDataUrl))
                {
                    return new ApiResponses().BadRequestResult($"{reg.FirstName} needs a signed waiver for this event.");
                }

                // Emergency contact gate, moved here from pre-payment: when the tenant
                // requires it, a registrant who holds any rider-audience ticket (race entry
                // or rider gate fee) must supply an emergency contact phone. Spectators are
                // exempt, mirroring the rider-only waiver/identity capture above.
                var isRiderRegistrant = loaded.Any(x =>
                    x.isRace || (x.tier.Kind == "gate_fee" && x.tier.Audience == "rider"));
                if (_tenantContext.Tenant.RequireEmergencyContact && isRiderRegistrant
                    && string.IsNullOrWhiteSpace(reg.EmergencyContactPhone))
                {
                    return new ApiResponses().BadRequestResult(
                        $"{reg.FirstName} needs an emergency contact phone number.");
                }

                // Minor / parent-guardian, enforced server-side (the counter path does the same). A
                // rider who must sign a waiver needs a birthdate so we can tell whether they're a minor;
                // a minor needs a parent/guardian name on the waiver. Without the birthdate requirement a
                // minor could be signed in as an adult simply by leaving the DOB blank.
                if (isRiderRegistrant && loaded.Any(x => x.needsWaiver))
                {
                    if (reg.Birthdate is null)
                    {
                        return new ApiResponses().BadRequestResult(
                            $"{reg.FirstName} needs a date of birth to sign the waiver.");
                    }
                    if (WaiverPolicy.IsMinor(reg.Birthdate) && string.IsNullOrWhiteSpace(reg.ParentGuardianName))
                    {
                        return new ApiResponses().BadRequestResult(
                            $"{reg.FirstName} is under 18 and needs a parent or guardian's name on the waiver.");
                    }
                }

                // One registrant id ties the rider's gate fee + their class entries together.
                var registrantId = Guid.NewGuid();

                // Resolve this registrant's effective waiver (the first waiver-bearing ticket's id,
                // else the tenant's active waiver), shared by all their tickets.
                Guid? registrantWaiverId = null;
                if (loaded.Any(x => x.needsWaiver))
                {
                    registrantWaiverId = loaded.First(x => x.needsWaiver).waiverId;
                    if (registrantWaiverId is null)
                    {
                        if (!activeFetched) { activeWaiverId = (await _waivers.GetActive(tenantId))?.Id; activeFetched = true; }
                        registrantWaiverId = activeWaiverId;
                    }
                }

                // Write ONE signature row per registrant to the shared rider_waiver_signature store,
                // then link it from each of this registrant's tickets. This is what makes the gate and
                // the "who has signed" report read one source of truth across the counter + online paths.
                Guid? registrantSignatureId = null;
                if (registrantWaiverId is not null && !string.IsNullOrWhiteSpace(reg.WaiverSignatureDataUrl))
                {
                    var isMinorRider = WaiverPolicy.IsMinor(reg.Birthdate);
                    var riderName = $"{reg.FirstName!.Trim()} {reg.LastName!.Trim()}".Trim();
                    var signerName = isMinorRider && !string.IsNullOrWhiteSpace(reg.ParentGuardianName)
                        ? reg.ParentGuardianName!.Trim()
                        : riderName;
                    var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
                    registrantSignatureId = await _waivers.SignRegistrant(tenantId, registrantWaiverId.Value, ip,
                        reg.WaiverSignatureDataUrl!, signerEmail: null, signerName: signerName,
                        attendeeFirstName: reg.FirstName!.Trim(), attendeeLastName: reg.LastName!.Trim(),
                        attendeeBirthdate: reg.Birthdate,
                        signedByParent: isMinorRider,
                        parentName: isMinorRider ? reg.ParentGuardianName?.Trim() : null,
                        parentPhone: null);
                }

                foreach (var x in loaded)
                {
                    Guid? waiverId = x.needsWaiver ? registrantWaiverId : null;
                    string? signature = x.needsWaiver ? reg.WaiverSignatureDataUrl : null;
                    Guid? signatureId = x.needsWaiver ? registrantSignatureId : null;

                    var wrote = await _ticketPurchases.CompleteRegistration(x.ticket.Id, tenantId,
                        riderFirstName: reg.FirstName!.Trim(), riderLastName: reg.LastName!.Trim(),
                        riderBirthdate: reg.Birthdate, bike: x.isRace ? reg.Bike?.Trim() : null,
                        raceNumber: x.isRace ? x.raceNumber?.Trim() : null,
                        waiverId: waiverId, waiverSignatureDataUrl: signature, waiverSignatureId: signatureId,
                        parentGuardianName: reg.ParentGuardianName?.Trim(),
                        emergencyContactName: isRiderRegistrant ? reg.EmergencyContactName?.Trim() : null,
                        emergencyContactPhone: isRiderRegistrant ? reg.EmergencyContactPhone?.Trim() : null,
                        registrantId: registrantId);
                    // Guarded write: a concurrent check-in can flip the ticket to 'redeemed' between the
                    // status check above and here. Treat that as a conflict rather than silently no-op.
                    if (!wrote)
                    {
                        return new ApiResponses().BadRequestResult(
                            "That ticket changed state (it may have just been checked in). Please reload and try again.");
                    }
                    completed++;
                }
            }

            // If the buyer is logged in and has no emergency contact on file yet, backfill their
            // account from the first one they entered here. Next time their profile pre-fills the
            // signup, so they don't re-key it. Only fills a BLANK account field (never overwrites).
            if (Guid.TryParse(User.FindFirst("UserId")?.Value, out var selfId))
            {
                var entered = request.Registrants.FirstOrDefault(r =>
                    !string.IsNullOrWhiteSpace(r.EmergencyContactName)
                    && !string.IsNullOrWhiteSpace(r.EmergencyContactPhone));
                if (entered is not null)
                {
                    // Best-effort: registration is already committed above, so a failure to
                    // backfill the profile must never turn a successful registration into an error.
                    try
                    {
                        var self = await _users.GetById(selfId);
                        if (self is not null && string.IsNullOrWhiteSpace(self.EmergencyContactPhone))
                        {
                            await _users.UpdateEmergencyContact(
                                selfId, entered.EmergencyContactName!.Trim(), entered.EmergencyContactPhone!.Trim());
                        }
                    }
                    catch { /* leave the profile as-is; the registration still succeeded */ }
                }
            }

            return new ApiResponses().OkResult(new { completed });
        }

        // Resume page (from the "finish your registration" email): given any ticket's
        // redemption token, return the still-incomplete entries in that order so the rider
        // can finish them. Guest-accessible; tenant-scoped by the resolved subdomain.
        [AllowAnonymous]
        [HttpGet("EventTicket/Registration/{token:guid}")]
        public async Task<IActionResult> GetRegistration(Guid token)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var rows = await _ticketPurchases.ListIncompleteForRegistrationByToken(token, _tenantContext.TenantId);
            var tickets = rows.Select(r =>
            {
                var isRace = r.Kind == "race_entry";
                var isRiderGate = r.Kind == "gate_fee" && r.Audience == "rider";
                var isSpectatorGate = r.Kind == "gate_fee" && r.Audience == "spectator";
                return new
                {
                    ticketId = r.TicketId,
                    tierName = r.TierName,
                    kind = r.Kind,
                    audience = r.Audience,
                    isRace,
                    isRiderGate,
                    isSpectatorGate,
                    needsWaiver = (isRace || isRiderGate) ? r.RequiresRiderWaiver : r.RequiresSpectatorWaiver,
                };
            }).ToList();
            return new ApiResponses().OkResult(new
            {
                eventTitle = rows.FirstOrDefault()?.EventTitle,
                tickets,
            });
        }

        private static (int amountCents, int serviceChargeCents) ComputeWithServiceCharge(
            int unitPriceCents, int quantity, int tenantServiceChargeBps, int riderPaidBps)
        {
            var serviceChargePerUnit = (int)((long)unitPriceCents * tenantServiceChargeBps / 10_000L);
            var riderPortionPerUnit = (int)((long)serviceChargePerUnit * riderPaidBps / 10_000L);
            var amount = (unitPriceCents + riderPortionPerUnit) * quantity;
            var serviceCharge = serviceChargePerUnit * quantity;
            return (amount, serviceCharge);
        }

        // Redeem ONE Loam Pass credit to cover a rider's entry to an event, instead of paying by
        // card. Only for a LoamPassMx track, a linked rider, a race_entry tier, and an event whose
        // type accepts Loam Pass (practice always; others per the tenant's event-type toggle).
        // Records the entry as paid at $0 via 'loampass_credits' (the track is reimbursed off-platform).
        [Authorize]
        [HttpPost("EventTicket/RedeemLoampass")]
        public async Task<IActionResult> RedeemLoampassForTicket([FromBody] RedeemLoampassTicketRequest request, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!TryGetUserId(out var userId)) return new ApiResponses().BadRequestResult("Invalid token.");

            var destinationId = _tenantContext.Tenant.LoampassMxDestinationId;
            if (string.IsNullOrWhiteSpace(destinationId))
                return new ApiResponses().BadRequestResult("This track doesn't accept Loam Pass credits.");

            var links = await _loampassLinks.ListByUserId(userId, _tenantContext.TenantId);
            if (links.Count == 0)
                return new ApiResponses().BadRequestResult("Connect your Loam Pass on your profile first.");

            var tier = await _tiers.GetById(request.TierId, _tenantContext.TenantId);
            if (tier is null || !tier.IsActive)
                return new ApiResponses().NotFoundResult("Ticket option not found.");
            if (tier.Kind != "race_entry")
                return new ApiResponses().BadRequestResult("Loam Pass credits cover rider entry only.");

            var ev = await _events.GetById(tier.EventId, _tenantContext.TenantId);
            if (ev is null || ev.Status != "scheduled")
                return new ApiResponses().NotFoundResult("Event not found.");

            var eventType = await _eventTypes.GetById(ev.EventTypeId, _tenantContext.TenantId);
            var typeAllows = eventType is not null && (eventType.Code == "practice" || eventType.AllowLoampassRedemption);
            if (!typeAllows)
                return new ApiResponses().BadRequestResult("Loam Pass credits aren't accepted for this event.");

            // Waiver gate — mirror the card buy flow so credit redeemers can't skip a required waiver.
            if (ev.RequiresRiderWaiver)
            {
                var activeWaiver = await _waivers.GetActive(_tenantContext.TenantId);
                if (activeWaiver is not null && await _waivers.GetSignature(userId, activeWaiver.Id) is null)
                    return new ApiResponses().BadRequestResult("You must sign the current waiver before redeeming a credit for this entry.");
            }

            var buyer = await _users.GetById(userId);

            // Capacity recheck + dedupe + pending-row insert under the same advisory lock the buy
            // flow uses, so concurrent requests can't oversell or let a rider double-enter (which
            // would also double-spend a credit). The lock is released before the network redeem call.
            Guid purchaseId;
            Guid redemptionToken;
            {
                await using var capacityLock = await _db.AcquireAdvisoryLock($"event-capacity:{ev.Id}");

                // Capacity: a price-ladder class carries no per-step inventory and sells against
                // event.capacity; a standalone tier additionally uses its own inventory. Mirrors
                // the card-buy and POS paths so the credit redeem can't oversell.
                var creditCap = tier.LadderGroup is null ? await EffectiveTierCap(tier) : null;
                if (creditCap is int creditCapValue)
                {
                    var sold = await _tiers.SoldCount(tier.Id);
                    if (sold + 1 > creditCapValue)
                        return new ApiResponses().BadRequestResult($"'{tier.Name}' is sold out.");
                }
                if (ev.Capacity.HasValue && tier.Audience == "rider")
                {
                    var eventSold = await _tiers.EventSoldCount(ev.Id, _tenantContext.TenantId);
                    if (eventSold + 1 > ev.Capacity.Value)
                        return new ApiResponses().BadRequestResult($"'{ev.Title}' is sold out.");
                }

                // One entry per rider per CLASS, spanning every step of a ladder group (not just the
                // scanned step), so a rider can't redeem two steps of the same class and double-spend
                // a credit. Standalone tiers check just themselves.
                var classStepIds = tier.LadderGroup is null
                    ? new List<Guid> { tier.Id }
                    : (await _tiers.GetForEvent(ev.Id, _tenantContext.TenantId, activeOnly: false))
                        .Where(t => t.LadderGroup == tier.LadderGroup).Select(t => t.Id).ToList();
                foreach (var stepId in classStepIds)
                {
                    if (await _ticketPurchases.HasActiveRaceEntry(_tenantContext.TenantId, stepId, userId, null))
                        return new ApiResponses().BadRequestResult("You're already entered in this class.");
                }

                var purchase = new EventTicketPurchase
                {
                    TenantId = _tenantContext.TenantId,
                    TierId = tier.Id,
                    PurchaserUserId = userId,
                    AmountCents = 0,
                    ServiceChargeCents = 0,
                    PaymentMethod = "loampass_credits",
                    Status = "pending",
                    PurchaserEmail = buyer?.Email ?? links[0].LoampassEmail,
                    PurchaserName = buyer is not null ? $"{buyer.FirstName} {buyer.LastName}".Trim() : string.Empty,
                };
                (purchaseId, redemptionToken) = await _ticketPurchases.Create(purchase);
            }

            // Redeem one credit on LoamMx, idempotent on the purchase id (a retry can't double-spend).
            // A rider may have several linked accounts; draw from the first that has a credit. Each
            // attempt writes a usage row only when it actually decrements, so trying several is safe.
            string? chargedAccountId = null;
            string? lastError = null;
            foreach (var l in links)
            {
                var attempt = await _loampass.RedeemAsync(l.LoampassAccountId, destinationId!, purchaseId.ToString(), ct);
                if (attempt.Redeemed) { chargedAccountId = l.LoampassAccountId; break; }
                lastError = attempt.Error;
            }
            if (chargedAccountId is null)
            {
                // No linked account had a credit — free the held spot; the rider keeps their credits.
                await _ticketPurchases.UpdateStatus(purchaseId, "cancelled");
                return new ApiResponses().BadRequestResult(lastError ?? "No Loam Pass credits available.");
            }

            // Record which account + key the credit came from so a refund reverses exactly this one.
            await _loampassRedemptions.Create(new Services.Repositories.Data.UserData.LoampassRedemption
            {
                TenantId = _tenantContext.TenantId,
                EventTicketPurchaseId = purchaseId,
                LoampassAccountId = chargedAccountId,
                DestinationId = destinationId!,
                IdempotencyKey = purchaseId.ToString(),
                Status = "redeemed",
            });

            // Finalize: paid + a $0 'loampass_credits' ledger row (the track is reimbursed off-platform).
            await _ticketPurchases.UpdateStatus(purchaseId, "paid");
            try
            {
                await _ledger.Insert(new Services.Repositories.Data.PaymentData.TenantLedgerEntry
                {
                    TenantId = _tenantContext.TenantId,
                    EntryKind = "sale",
                    SourceKind = "event_ticket",
                    SourceId = purchaseId,
                    OccurredAtUtc = DateTime.UtcNow,
                    GrossCents = 0,
                    StripeFeeCents = 0,
                    RidepassCutCents = 0,
                    NetToTenantCents = 0,
                    PaymentMethod = "loampass_credits",
                    Memo = "Loam Pass credit redeemed for rider entry",
                });
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
            {
                // Idempotent — duplicate sale row for this source.
            }

            // Paid with a Loam Pass credit rather than money, but it's the same admission and the
            // rider still needs their QR. No PaymentIntent here either, so confirm inline.
            await _orderConfirmations.SendForTickets(_tenantContext.TenantId, new[] { purchaseId });

            return new ApiResponses().OkResult(new CreatePurchaseResponse
            {
                PurchaseId = purchaseId,
                RedemptionToken = redemptionToken,
                ClientSecret = string.Empty,
                AmountCents = 0,
                RiderServiceChargeCents = 0,
            });
        }

        // Tenant-admin refund of a single purchase (gift cards excluded; rentals/concessions out of
        // scope). AmountCents null = full-minus-service-charge default. ForceCheckedIn (requires
        // sales.refund.override) refunds even an already-checked-in entry. The money + state changes
        // live in RefundOne so the whole-order endpoint shares the exact same behavior.
        [Authorize(Policy = TenantPermissions.Policy.SalesRefund)]
        [HttpPost("Refund")]
        public async Task<IActionResult> Refund([FromBody] RefundRequest request, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!TryGetUserId(out var staffId)) return new ApiResponses().BadRequestResult("Invalid token.");
            if (request.ForceCheckedIn && !HasRefundOverride())
                return new ApiResponses().BadRequestResult("You don't have permission to refund a checked-in purchase.");

            var result = await RefundOne(_tenantContext.TenantId, request.Kind, request.PurchaseId,
                request.AmountCents, request.Reason, staffId,
                allowCheckedIn: request.ForceCheckedIn, fullAmount: false, ct);
            if (!result.ok) return new ApiResponses().BadRequestResult(result.error ?? "Refund failed.");
            return new ApiResponses().OkResult(new { refunded = true, kind = request.Kind, amountCents = result.refundCents, refundId = result.refundId });
        }

        // Refund every line on the same order: all purchases sharing the anchor's Stripe
        // PaymentIntent (race entry + gate fee + add-ons + bundled membership/season pass). Each line
        // is refunded IN FULL (including the service charge). Cash/free orders have no shared
        // PaymentIntent, so only the anchor item is refunded there.
        [Authorize(Policy = TenantPermissions.Policy.SalesRefund)]
        [HttpPost("RefundOrder")]
        public async Task<IActionResult> RefundOrder([FromBody] RefundOrderRequest request, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!TryGetUserId(out var staffId)) return new ApiResponses().BadRequestResult("Invalid token.");
            if (request.ForceCheckedIn && !HasRefundOverride())
                return new ApiResponses().BadRequestResult("You don't have permission to refund a checked-in purchase.");
            var tenantId = _tenantContext.TenantId;

            var pi = await ResolvePaymentIntentForPurchase(request.Kind, request.PurchaseId, tenantId);

            var lines = new List<(string Kind, Guid Id)>();
            if (string.IsNullOrEmpty(pi))
            {
                // No shared PaymentIntent (cash / fully-gift-card-covered order): refund the one item.
                lines.Add((request.Kind, request.PurchaseId));
            }
            else
            {
                foreach (var t in await _ticketPurchases.ListByStripePaymentIntentId(pi))
                    lines.Add(("event_ticket", t.Id));
                foreach (var x in await _extras.ListByPaymentIntentId(pi))
                    lines.Add(("event_extra", x.Id));
                var m = await _memberships.GetByPaymentIntentId(pi);
                if (m is not null) lines.Add(("membership", m.Id));
                // Every pass on the intent, not just the first: one checkout can buy several, and a
                // whole-order refund that stopped at the first would leave the rest live and paid.
                foreach (var sp in await _seasonPasses.ListPurchasesByStripePaymentIntentId(pi))
                    lines.Add(("season_pass", sp.Id));
            }

            int refundedCount = 0, totalCents = 0;
            var errors = new List<string>();
            foreach (var (kind, id) in lines)
            {
                var r = await RefundOne(tenantId, kind, id, amountCents: null, request.Reason, staffId,
                    allowCheckedIn: request.ForceCheckedIn, fullAmount: true, ct);
                if (r.ok) { refundedCount++; totalCents += r.refundCents; }
                else if (!r.alreadyDone && r.error is not null) errors.Add(r.error);   // skip already-refunded lines silently
            }

            if (refundedCount == 0 && errors.Count > 0)
                return new ApiResponses().BadRequestResult(string.Join(" ", errors));
            return new ApiResponses().OkResult(new { refundedCount, totalCents, errors });
        }

        // Refund a caller-selected set of order lines, each in full (incl. service charge). Backs the
        // Order details refund modal's row selection. RefundOne validates each line is in the tenant,
        // so a spoofed id from another tenant resolves to "Purchase not found" and is skipped.
        [Authorize(Policy = TenantPermissions.Policy.SalesRefund)]
        [HttpPost("RefundLines")]
        public async Task<IActionResult> RefundLines([FromBody] RefundLinesRequest request, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!TryGetUserId(out var staffId)) return new ApiResponses().BadRequestResult("Invalid token.");
            if (request.Lines is null || request.Lines.Count == 0)
                return new ApiResponses().BadRequestResult("No items selected to refund.");
            if (request.ForceCheckedIn && !HasRefundOverride())
                return new ApiResponses().BadRequestResult("You don't have permission to refund a checked-in purchase.");
            var tenantId = _tenantContext.TenantId;

            int refundedCount = 0, totalCents = 0;
            var errors = new List<string>();
            foreach (var line in request.Lines)
            {
                var r = await RefundOne(tenantId, line.Kind, line.Id, amountCents: null, request.Reason, staffId,
                    allowCheckedIn: request.ForceCheckedIn, fullAmount: true, ct);
                if (r.ok) { refundedCount++; totalCents += r.refundCents; }
                else if (!r.alreadyDone && r.error is not null) errors.Add(r.error);   // skip already-refunded lines silently
            }

            if (refundedCount == 0 && errors.Count > 0)
                return new ApiResponses().BadRequestResult(string.Join(" ", errors));
            return new ApiResponses().OkResult(new { refundedCount, totalCents, errors });
        }

        // True when the caller may refund a checked-in / used purchase. super_admin always may;
        // otherwise the role must carry sales.refund.override (tenant_admin + tenant_manager).
        private bool HasRefundOverride()
        {
            var role = User.FindFirst("role")?.Value ?? "";
            if (role == "super_admin") return true;
            return TenantPermissions.ForRole(role).Contains(TenantPermissions.SalesRefundOverride);
        }

        // Resolve any purchase to the Stripe PaymentIntent that charged its order (null for
        // cash / fully-gift-card-covered orders). Tenant-scoped.
        private async Task<string?> ResolvePaymentIntentForPurchase(string kind, Guid id, Guid tenantId)
        {
            switch (kind)
            {
                case "event_ticket":
                    return (await _ticketPurchases.GetById(id, tenantId))?.StripePaymentIntentId;
                case "event_extra":
                    var x = await _extras.GetPurchase(id);
                    return x?.TenantId == tenantId ? x.StripePaymentIntentId : null;
                case "membership":
                    var m = await _memberships.GetById(id);
                    return m?.TenantId == tenantId ? m.StripePaymentIntentId : null;
                case "season_pass":
                    var sp = await _seasonPasses.GetPurchase(id);
                    return sp?.TenantId == tenantId ? sp.StripePaymentIntentId : null;
                default:
                    return null;
            }
        }

        // Shared single-purchase refund: resolve the row, move the money (Stripe refund / Loam Pass
        // un-redeem), cancel + mark refunded, tear down entitlements, promote a waitlist alternate, and
        // write the refund ledger row. fullAmount=true refunds the whole charge (incl. service charge);
        // otherwise AmountCents (or amount-minus-service-charge) applies. allowCheckedIn lets a
        // 'redeemed' (checked-in) row be refunded. Returns alreadyDone=true for an already
        // refunded/cancelled row so a whole-order refund can skip it silently.
        private async Task<(bool ok, bool alreadyDone, string? error, int refundCents, string? refundId)> RefundOne(
            Guid tenantId, string kind, Guid purchaseId, int? amountCents, string? reason,
            Guid staffId, bool allowCheckedIn, bool fullAmount, CancellationToken ct)
        {
            int amount = 0, serviceCharge = 0;
            string paymentMethod = "stripe", status = "";
            string? stripePi = null;
            // Connected account a direct charge was made on (NULL = platform charge). Refunds for a
            // direct charge must be issued on that account, returning our application fee too.
            string? connectedAccount = null;
            string ledgerSourceKind;
            Guid eventTicketTierId = Guid.Empty;
            // Funding "credits" season pass of a credit-covered ticket — the refund hands the
            // ride back (mirrors the Loam Pass credit return above).
            Guid? appliedSeasonPassId = null;

            switch (kind)
            {
                case "event_ticket":
                {
                    var p = await _ticketPurchases.GetById(purchaseId, tenantId);
                    if (p is null) return (false, false, "Purchase not found.", 0, null);
                    (amount, serviceCharge, paymentMethod, status, stripePi) = (p.AmountCents, p.ServiceChargeCents, p.PaymentMethod, p.Status, p.StripePaymentIntentId);
                    connectedAccount = p.StripeConnectedAccountId;
                    eventTicketTierId = p.TierId;
                    appliedSeasonPassId = p.AppliedSeasonPassPurchaseId;
                    ledgerSourceKind = "event_ticket";
                    break;
                }
                case "season_pass":
                {
                    var p = await _seasonPasses.GetPurchase(purchaseId);
                    if (p is null || p.TenantId != tenantId) return (false, false, "Purchase not found.", 0, null);
                    (amount, serviceCharge, paymentMethod, status, stripePi) = (p.AmountCents, p.ServiceChargeCents, p.PaymentMethod, p.Status, p.StripePaymentIntentId);
                    connectedAccount = p.StripeConnectedAccountId;
                    ledgerSourceKind = "season_pass";
                    break;
                }
                case "membership":
                {
                    var p = await _memberships.GetById(purchaseId);
                    if (p is null || p.TenantId != tenantId) return (false, false, "Purchase not found.", 0, null);
                    (amount, serviceCharge, paymentMethod, status, stripePi) = (p.AmountCents, p.ServiceChargeCents, p.PaymentMethod, p.Status, p.StripePaymentIntentId);
                    connectedAccount = p.StripeConnectedAccountId;
                    ledgerSourceKind = "membership";
                    break;
                }
                case "event_extra":
                {
                    var p = await _extras.GetPurchase(purchaseId);
                    if (p is null || p.TenantId != tenantId) return (false, false, "Purchase not found.", 0, null);
                    (amount, serviceCharge, paymentMethod, status, stripePi) = (p.AmountCents, p.ServiceChargeCents, p.PaymentMethod, p.Status, p.StripePaymentIntentId);
                    connectedAccount = p.StripeConnectedAccountId;
                    ledgerSourceKind = "extras";   // ledger uses 'extras'; v_recent_sales kind is 'event_extra'
                    break;
                }
                default:
                    return (false, false, "This purchase type can't be refunded here.", 0, null);
            }

            if (status is "refunded" or "cancelled")
                return (false, true, "This purchase has already been refunded or cancelled.", 0, null);
            if (status != "paid" && !(allowCheckedIn && status == "redeemed"))
                return (false, false, status == "redeemed"
                    ? "This purchase is checked in; refunding it requires elevated permission."
                    : "Only a paid purchase can be refunded.", 0, null);

            // Snapshot before the refund rewrites it: the audit entry at the end of this method
            // reports what the purchase was BEFORE being refunded, and "already checked in" is the
            // detail a reviewer cares about most.
            var wasRedeemed = status == "redeemed";

            // fullAmount (whole-order) refunds everything including the service charge; otherwise the
            // explicit amount, else the single-refund default (amount minus the service charge).
            var refundCents = amountCents ?? (fullAmount ? amount : Math.Max(0, amount - serviceCharge));
            if (refundCents < 0) refundCents = 0;
            if (refundCents > amount) refundCents = amount;

            // Card-first split for orders a gift card partially covered. The card only ever received
            // (amount - giftCardPortion), so refund that to Stripe first and return only the overflow
            // to the gift card. giftCardPortion is 0 for kinds a gift card never touches.
            var giftCardPortion = (kind == "event_ticket" || kind == "season_pass")
                ? await _giftCards.SumRedemptionsForSource(kind, purchaseId, tenantId)
                : 0;
            var cardChargedCents = Math.Max(0, amount - giftCardPortion);
            var stripeRefundCents = Math.Min(refundCents, cardChargedCents);
            var giftCardRefundCents = refundCents - stripeRefundCents;

            string? refundId = null;
            if (paymentMethod == "loampass_credits")
            {
                var redemption = await _loampassRedemptions.GetByPurchaseId(purchaseId, tenantId);
                if (redemption is not null && redemption.Status != "refunded")
                {
                    var returned = await _loampass.RefundAsync(redemption.IdempotencyKey, ct);
                    if (!returned)
                        return (false, false, "Couldn't return the Loam Pass credit on the LoamMx side. The purchase was left unchanged; please retry.", 0, null);
                    await _loampassRedemptions.MarkRefunded(redemption.Id);
                }
                refundCents = 0;   // no money moved; the credit is given back
            }
            else if ((paymentMethod == "stripe" || paymentMethod == "stripe_connect" || paymentMethod == "stripe_direct")
                     && !string.IsNullOrEmpty(stripePi) && stripeRefundCents > 0)
            {
                // Direct charge: issue the refund on the tenant's connected account and return our
                // application fee on the refunded portion so the tenant is made whole.
                var isDirect = paymentMethod == "stripe_direct";
                try
                {
                    var r = await _payments.RefundAsync(stripePi!, stripeRefundCents,
                        idempotencyKey: $"refund-{kind}-{purchaseId}-{stripeRefundCents}",
                        connectedAccountId: isDirect ? connectedAccount : null,
                        refundApplicationFee: isDirect,
                        ct: ct);
                    refundId = r.RefundId;
                }
                catch (Exception ex)
                {
                    return (false, false, $"Refund failed at the payment processor: {ex.Message}", 0, null);
                }
            }
            // cash / voucher: nothing to move.

            var note = $"Tenant refund {refundCents}c{(refundId is null ? "" : $" stripe={refundId}")}";
            switch (kind)
            {
                case "event_ticket":
                    // A checked-in (redeemed) ticket must be flipped back to 'paid' first so Cancel
                    // records the cancellation audit; MarkRefunded then finalizes it as 'refunded'.
                    if (status == "redeemed") await _ticketPurchases.UndoRedeemed(purchaseId, tenantId);
                    await _ticketPurchases.Cancel(purchaseId, tenantId, staffId, reason);
                    await _ticketPurchases.MarkRefunded(purchaseId, note);
                    // Credit-funded ticket: hand the ride back to the funding pass. Idempotent
                    // against double-refund via the alreadyDone short-circuit above, and a no-op
                    // when the pass itself was refunded (status guard inside IncrementCredits).
                    if (appliedSeasonPassId.HasValue)
                        await _seasonPasses.IncrementCredits(appliedSeasonPassId.Value, tenantId);
                    // A refund frees a spot, so promote the next waitlist alternate.
                    var refundedTier = await _tiers.GetById(eventTicketTierId, tenantId);
                    if (refundedTier is not null)
                        _ = _waitlistPromoter.PromoteNext(refundedTier.EventId, eventTicketTierId, refundedTier.LadderGroup);
                    break;
                case "season_pass":
                    await _seasonPasses.Cancel(purchaseId, tenantId, staffId, reason);
                    // Release the pass's reservations so it no longer holds event spots.
                    foreach (var rsv in (await _seasonPasses.ListReservationsForPurchase(purchaseId))
                                 .Where(x => x.Status != "cancelled"))
                    {
                        await _seasonPasses.UpdateReservationStatus(rsv.Id, tenantId, "cancelled");
                    }
                    await _seasonPasses.MarkRefunded(purchaseId, note);
                    break;
                case "membership":
                    await _memberships.Cancel(purchaseId, tenantId, staffId, reason);
                    await _memberships.MarkRefunded(purchaseId);
                    break;
                case "event_extra":
                    await _extras.Cancel(purchaseId, tenantId, staffId, reason);
                    await _extras.MarkRefunded(purchaseId, note);
                    break;
            }

            // Return the gift-card share of the refund to the card it came from (card-first split).
            if (giftCardRefundCents > 0)
                await RestoreGiftCardOnRefund(kind, purchaseId, tenantId, giftCardRefundCents);

            // Refund ledger amounts depend on how the sale was funded:
            //  - stripe / stripe_connect: the platform moved the money, so the refund debits the
            //    tenant's payout by the refunded amount (net = -refundCents). Platform cut/fees
            //    aren't clawed back (a tenant-initiated refund leaves the prior cut as-is).
            //  - cash / voucher: the tenant always held the cash; the platform only ever booked its
            //    cut (the cash sale recorded net = -serviceCharge). Debiting the tenant the full
            //    cash amount here would reduce their payout by money the platform never touched, so
            //    instead reverse the proportional cut, which nets a fully-refunded cash sale to zero.
            //  - loampass_credits: refundCents is already 0 (the credit was returned, no cash moved).
            // Refund ledger amounts depend on how the sale was funded — see RefundLedgerMath. isDirect
            // is only consulted for the gift-card-funded branch (a direct-mode tenant already holds the
            // gift-card float); the stripe_direct case is keyed off paymentMethod itself.
            var isDirectRefund = _tenantContext.IsResolved && _tenantContext.Tenant?.StripeChargeMode == "direct";
            var ledgerAmounts = Services.Payments.RefundLedgerMath.Compute(
                paymentMethod, amount, serviceCharge, refundCents, stripeRefundCents, giftCardRefundCents, isDirectRefund);
            var ledgerGross = ledgerAmounts.GrossCents;
            var ledgerCut = ledgerAmounts.RidepassCutCents;
            var ledgerNet = ledgerAmounts.NetToTenantCents;
            try
            {
                await _ledger.Insert(new Services.Repositories.Data.PaymentData.TenantLedgerEntry
                {
                    TenantId = tenantId,
                    EntryKind = "refund",
                    SourceKind = ledgerSourceKind,
                    SourceId = purchaseId,
                    OccurredAtUtc = DateTime.UtcNow,
                    GrossCents = ledgerGross,
                    StripeFeeCents = 0,
                    RidepassCutCents = ledgerCut,
                    NetToTenantCents = ledgerNet,
                    PaymentMethod = paymentMethod,
                    SoldByUserId = staffId,
                    Memo = $"Tenant refund{(refundId is null ? "" : $" {refundId}")}",
                });
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
            {
                // Idempotent — duplicate refund row for this source.
            }

            // Every refund path (Refund, RefundOrder, RefundLines) funnels through here, so this is
            // the one place that sees them all. Written after the refund has fully committed, and
            // best-effort by contract, so a failed audit write can never fail a completed refund.
            //
            // The metadata is chosen for after-the-fact review of staff behavior, not for debugging:
            // a cash refund carries no Stripe refund id and leaves no trace with the processor, so
            // (paymentMethod = cash, stripeRefundId = null) is the shape worth watching. Refunding a
            // purchase that was already checked in is the other one: the customer already rode.
            await _audit.Log(
                "purchase.refund",
                $"Refunded ${refundCents / 100m:0.00} on a {kind.Replace('_', ' ')} "
                    + $"({paymentMethod}{(wasRedeemed ? ", already checked in" : "")})",
                targetKind: kind,
                targetId: purchaseId,
                tenantId: tenantId,
                metadata: new
                {
                    kind,
                    refundCents,
                    originalAmountCents = amount,
                    serviceChargeCents = serviceCharge,
                    // The tender the customer originally paid with. A cash refund is money out of
                    // the till with no processor record.
                    paymentMethod,
                    // Null for cash/manual refunds; present for anything Stripe actually reversed.
                    stripeRefundId = refundId,
                    // Set when the sale ran on the tenant's own connected account (direct charge).
                    connectedAccount,
                    // True when staff refunded a purchase that had already been redeemed, which
                    // requires the sales.refund.override permission.
                    wasCheckedIn = wasRedeemed,
                    fullAmount,
                    reason,
                });

            return (true, false, null, refundCents, refundId);
        }

        // Returns the gift-card share of a refund to the card it came from. Deletes the purchase's
        // redemption rows and restores the balance; on a partial refund (restore < applied) it
        // re-records the still-applied remainder so the redemption audit matches the new balance.
        // Normally one card / one row per purchase; the loop tolerates the multi-card case.
        private async Task RestoreGiftCardOnRefund(string sourceKind, Guid purchaseId, Guid tenantId, int restoreCents)
        {
            if (restoreCents <= 0) return;
            var rows = await _giftCards.DeleteRedemptionsBySource(sourceKind, new[] { purchaseId });
            var total = rows.Sum(r => r.AmountCents);
            if (total <= 0) return;

            var restored = 0;
            var cards = rows.GroupBy(r => r.GiftCardId).ToList();
            for (int i = 0; i < cards.Count; i++)
            {
                var byCard = cards[i];
                var applied = byCard.Sum(r => r.AmountCents);
                // Distribute proportionally across cards (last absorbs the rounding remainder),
                // capped at what that card actually applied.
                var cardRestore = i == cards.Count - 1
                    ? Math.Min(applied, restoreCents - restored)
                    : (int)((long)restoreCents * applied / total);
                restored += cardRestore;
                if (cardRestore > 0) await _giftCards.RestoreBalance(byCard.Key, cardRestore);

                var remaining = applied - cardRestore;
                if (remaining > 0)
                {
                    await _giftCards.RecordRedemption(new GiftCardRedemption
                    {
                        GiftCardId = byCard.Key,
                        TenantId = tenantId,
                        UserId = byCard.First().UserId,
                        SourceKind = sourceKind,
                        SourceId = purchaseId,
                        AmountCents = remaining,
                    });
                }
            }
        }

        // Validates a drawn-signature data URL (mirrors CounterController.IsValidPngDataUrl): a PNG data
        // URL within a sane size band. Guards against an unbounded/garbage string being stored as the
        // waiver signature via the anonymous CompleteRegistration endpoint.
        private static bool IsValidSignatureDataUrl(string? dataUrl)
        {
            if (string.IsNullOrWhiteSpace(dataUrl)) return false;
            if (!dataUrl.StartsWith("data:image/png;base64,", StringComparison.Ordinal)) return false;
            return dataUrl.Length is > 800 and < 1_400_000;
        }

        // The credit tender's balancing entry for the immediately-settled free path (a card
        // checkout books it from the payment webhook instead). Gross -credit corrects the row
        // grosses; net reduces only platform-held money (direct-mode rows already carry 0).
        private async Task BookCreditTenderEntryNow(Guid tenderId, int creditCents)
        {
            var isDirect = _tenantContext.Tenant?.StripeChargeMode == "direct";
            try
            {
                await _ledger.Insert(new Services.Repositories.Data.PaymentData.TenantLedgerEntry
                {
                    TenantId = _tenantContext.TenantId,
                    EntryKind = "sale",
                    SourceKind = "credit_tender",
                    SourceId = tenderId,
                    OccurredAtUtc = DateTime.UtcNow,
                    GrossCents = -creditCents,
                    StripeFeeCents = 0,
                    RidepassCutCents = 0,
                    NetToTenantCents = isDirect == true ? 0 : -creditCents,
                    PaymentMethod = "credit",
                    Memo = "Store credit applied at checkout",
                });
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505") { /* idempotent */ }
        }

        private async Task InsertZeroLedger(Guid tenantId, string sourceKind, Guid sourceId)
        {
            try
            {
                await _ledger.Insert(new Services.Repositories.Data.PaymentData.TenantLedgerEntry
                {
                    TenantId = tenantId,
                    EntryKind = "sale",
                    SourceKind = sourceKind,
                    SourceId = sourceId,
                    OccurredAtUtc = DateTime.UtcNow,
                    GrossCents = 0,
                    StripeFeeCents = 0,
                    RidepassCutCents = 0,
                    NetToTenantCents = 0,
                    PaymentMethod = "voucher",
                    // 'voucher' is the long-standing ledger vocabulary for a zero-charge sale and
                    // is read by the accounting view, so the VALUE stays; only the memo changes,
                    // because the feature it named no longer exists.
                    Memo = "Free purchase (no charge)",
                });
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
            {
                // Idempotent — duplicate sale row for this source.
            }
        }

        // A gift card fully covered this sale. Unlike a zero-priced cart this is NOT free: the buyer paid
        // real money for the card, so the tenant is owed the value delivered. There's no Stripe fee at
        // redemption (no card charge happens now — it happened when the card was bought).
        //   • Platform-charge tenant: the platform holds the gift-card float, so credit the tenant the
        //     normal net (gross minus our cut) and book our cut.
        //   • Direct-charge tenant: the float already sits in the tenant's own account (gift cards for
        //     direct tenants are sold there) and our fee was taken at sale time, so net = 0 / cut = 0.
        private async Task InsertGiftCardCoveredLedger(Guid tenantId, string sourceKind, Guid sourceId, int grossCents, int serviceChargeCents)
        {
            var isDirect = _tenantContext.IsResolved && _tenantContext.Tenant?.StripeChargeMode == "direct";
            int cut, net;
            if (isDirect)
            {
                cut = 0;
                net = 0;
            }
            else
            {
                var calc = await _feeCalculator.Calculate(tenantId, grossCents, 0, serviceChargeCents, DateTime.UtcNow, isDirect: false);
                cut = calc.RidepassCutCents;
                net = calc.NetToTenantCents;
            }
            try
            {
                await _ledger.Insert(new Services.Repositories.Data.PaymentData.TenantLedgerEntry
                {
                    TenantId = tenantId,
                    EntryKind = "sale",
                    SourceKind = sourceKind,
                    SourceId = sourceId,
                    OccurredAtUtc = DateTime.UtcNow,
                    GrossCents = grossCents,
                    StripeFeeCents = 0,
                    RidepassCutCents = cut,
                    NetToTenantCents = net,
                    PaymentMethod = "voucher",   // funded by a gift card, no card charge at redemption
                    Memo = "Gift card covered purchase",
                });
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
            {
                // Idempotent — duplicate sale row for this source.
            }
        }

        [Authorize(Policy = TenantPermissions.Policy.SalesView)]
        [HttpGet("Admin")]
        public async Task<IActionResult> ListForAdmin(
            [FromQuery] DateTime? fromUtc,
            [FromQuery] DateTime? toUtc,
            [FromQuery] string? email,
            [FromQuery] string? orderId,
            [FromQuery] List<string>? statuses,
            [FromQuery] List<string>? kinds,
            [FromQuery] int offset = 0,
            [FromQuery] int limit = 50,
            [FromQuery] bool includeAbandoned = false)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            // Reads from v_recent_sales (Script0080) so every sale kind shows up
            // - day passes, event tickets, gate fees, season passes, memberships,
            // gift cards, rentals. `statuses` absent/empty defaults to everything
            // except 'abandoned' (our own reconciler giving up on a checkout that
            // never saw a completed payment attempt, not a real decline); pass
            // 'abandoned' explicitly to include it, or set includeAbandoned to lift the
            // exclusion without naming statuses (the view's eight branches do not share one
            // status vocabulary, so "list them all" is not a thing a caller can safely do).
            //
            // When the admin searches by email or order id, drop the date window so a
            // match outside the default last-month range still surfaces (the frontend
            // only falls back to the server when the loaded range has no match).
            var searching = !string.IsNullOrWhiteSpace(email) || !string.IsNullOrWhiteSpace(orderId);
            var from = searching ? (DateTime?)null : fromUtc;
            var to = searching ? (DateTime?)null : toUtc;

            // Never trust client-supplied paging: a negative offset or an unbounded
            // limit would either error downstream or let a stray query pull the whole
            // table. Hard cap at 200 regardless of what the client asks for.
            var safeOffset = Math.Max(offset, 0);
            var safeLimit = Math.Clamp(limit <= 0 ? 50 : limit, 1, 200);

            var (rows, total) = await _recentSales.List(_tenantContext.TenantId, from, to, statuses, kinds, safeOffset, safeLimit, email, orderId, includeAbandoned);
            var response = rows.Select(r => new PurchaseResponse
            {
                Id = r.Id,
                Kind = r.Kind,
                ProductName = r.ItemName ?? string.Empty,
                PurchaserName = r.PurchaserName ?? string.Empty,
                PurchaserEmail = r.PurchaserEmail ?? string.Empty,
                AmountCents = r.AmountCents,
                Status = r.Status,
                CreatedAt = DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc),
                RedemptionToken = r.RedemptionToken?.ToString(),
            }).ToList();
            return new ApiResponses().OkResult(new
            {
                rows = response,
                total,
                offset = safeOffset,
                limit = safeLimit
            });
        }

        // Every line in one order (all sales sharing the anchor's Stripe PaymentIntent), for the
        // Admin Purchases "Order details" modal. Tenant-scoped in the repository.
        [Authorize(Policy = TenantPermissions.Policy.SalesView)]
        [HttpGet("Admin/Order")]
        public async Task<IActionResult> ListOrderForAdmin([FromQuery] string kind, [FromQuery] Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (string.IsNullOrWhiteSpace(kind) || id == Guid.Empty)
                return new ApiResponses().BadRequestResult("kind and id are required.");
            var rows = await _recentSales.ListOrder(_tenantContext.TenantId, kind, id);
            var response = rows.Select(r => new PurchaseResponse
            {
                Id = r.Id,
                Kind = r.Kind,
                ProductName = r.ItemName ?? string.Empty,
                PurchaserName = r.PurchaserName ?? string.Empty,
                PurchaserEmail = r.PurchaserEmail ?? string.Empty,
                AmountCents = r.AmountCents,
                Status = r.Status,
                CreatedAt = DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc),
                RedemptionToken = r.RedemptionToken?.ToString(),
            });
            return new ApiResponses().OkResult(response);
        }

        [Authorize(Policy = TenantPermissions.Policy.SalesCancel)]
        [HttpPost("Ticket/{id:guid}/Cancel")]
        public async Task<IActionResult> CancelTicket(Guid id, [FromBody] CancelPurchaseRequest request)
        {
            var existing = await _ticketPurchases.GetById(id, _tenantContext.TenantId);
            if (existing is null)
            {
                return new ApiResponses().NotFoundResult("Ticket not found.");
            }
            if (existing.Status != "paid")
            {
                return new ApiResponses().BadRequestResult($"Cannot cancel a ticket with status '{existing.Status}'.");
            }
            if (!TryGetUserId(out var adminId))
            {
                return new ApiResponses().BadRequestResult("Invalid token.");
            }

            await _ticketPurchases.Cancel(id, _tenantContext.TenantId, adminId, request.Reason);
            // Promote the next alternate waiting on this class. For a price ladder the bucket
            // is the ladder group (so any step's refund frees the class); otherwise it's the
            // exact tier (Pro division etc.). Need the event id to scope.
            var tier = await _tiers.GetById(existing.TierId, _tenantContext.TenantId);
            if (tier is not null)
            {
                _ = _waitlistPromoter.PromoteNext(tier.EventId, existing.TierId, tier.LadderGroup);
            }

            // Cancelling voids a paid sale without moving money through Stripe, so it is the other
            // way to make a sale disappear and belongs in the same review surface as refunds.
            await _audit.Log(
                "purchase.cancel",
                $"Cancelled a paid ticket worth ${existing.AmountCents / 100m:0.00}",
                targetKind: "event_ticket",
                targetId: id,
                tenantId: _tenantContext.TenantId,
                metadata: new
                {
                    kind = "event_ticket",
                    amountCents = existing.AmountCents,
                    paymentMethod = existing.PaymentMethod,
                    reason = request.Reason,
                });

            return new ApiResponses().OkResult(new { id, status = "cancelled" });
        }

        [Authorize(Policy = TenantPermissions.Policy.DisputesView)]
        [HttpGet("Admin/Disputes")]
        public async Task<IActionResult> ListDisputes()
        {
            var rows = await _disputes.ListByTenant(_tenantContext.TenantId);
            var items = rows.Select(d => new TenantDisputeListItem
            {
                Id = d.Id,
                Kind = d.EventTicketPurchaseId.HasValue ? "event_ticket" : "unlinked",
                PurchaseId = d.EventTicketPurchaseId,
                ItemName = d.ItemName,
                PurchaserName = d.PurchaserName,
                PurchaserEmail = d.PurchaserEmail,
                StripeDisputeId = d.StripeDisputeId,
                AmountCents = d.AmountCents,
                Currency = d.Currency,
                Reason = d.Reason,
                Status = d.Status,
                EvidenceDueByUtc = d.EvidenceDueBy.HasValue ? DateTime.SpecifyKind(d.EvidenceDueBy.Value, DateTimeKind.Utc) : null,
                StripeCreatedAtUtc = DateTime.SpecifyKind(d.StripeCreatedAt, DateTimeKind.Utc),
                UpdatedAtUtc = DateTime.SpecifyKind(d.UpdatedAt, DateTimeKind.Utc),
            });
            return new ApiResponses().OkResult(items);
        }

        private bool TryGetUserId(out Guid userId)
        {
            var claim = User.FindFirst("UserId")?.Value;
            return Guid.TryParse(claim, out userId);
        }


        // ── Gift cards ──────────────────────────────────────────────────────────
        // Buyer chooses denomination + recipient + optional schedule + note. We mint
        // the gift_card row up front as status='pending' (NOT spendable or deliverable)
        // so the payment intent can reference it; the webhook activates it + sends the
        // email (or schedules it via the delivery worker) on payment_intent.succeeded,
        // and voids it on failure, so a declined/abandoned purchase never yields a live card.

        [Authorize]
        [HttpPost("GiftCard")]
        public async Task<IActionResult> BuyGiftCard([FromBody] BuyGiftCardRequest request, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!TryGetUserId(out var userId))
                return new ApiResponses().BadRequestResult("Invalid token.");

            var tenant = _tenantContext.Tenant;
            if (!tenant.GiftCardsEnabled)
                return new ApiResponses().BadRequestResult("This tenant doesn't sell gift cards.");

            if (request.AmountCents < tenant.GiftCardMinCents || request.AmountCents > tenant.GiftCardMaxCents)
            {
                return new ApiResponses().BadRequestResult(
                    $"Gift card amount must be between ${tenant.GiftCardMinCents / 100m:0.00} and ${tenant.GiftCardMaxCents / 100m:0.00}.");
            }

            var buyer = await _users.GetById(userId);
            if (buyer is null) return new ApiResponses().BadRequestResult("Buyer not found.");

            // Generate a unique code with retry. Reuses CouponCodeGenerator's alphabet —
            // GIFT- prefix + 8 chars from a no-confusing-chars set.
            string code = string.Empty;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                var candidate = Services.Coupons.CouponCodeGenerator.Generate("GIFT");
                if (await _giftCards.GetByCode(_tenantContext.TenantId, candidate) is null)
                {
                    code = candidate;
                    break;
                }
            }
            if (string.IsNullOrEmpty(code))
                return new ApiResponses().BadRequestResult("Could not generate a unique code, please retry.");

            var card = new Services.Repositories.Data.GiftCardData.GiftCard
            {
                TenantId = _tenantContext.TenantId,
                Code = code,
                InitialAmountCents = request.AmountCents,
                BalanceCents = request.AmountCents,
                BuyerUserId = userId,
                BuyerName = $"{buyer.FirstName} {buyer.LastName}".Trim(),
                BuyerEmail = buyer.Email,
                RecipientName = request.RecipientName.Trim(),
                RecipientEmail = request.RecipientEmail.Trim(),
                PersonalNote = string.IsNullOrWhiteSpace(request.PersonalNote) ? null : request.PersonalNote.Trim(),
                DeliveryStatus = "pending",
                ScheduledDeliveryAtUtc = request.ScheduledDeliveryAtUtc,
                Status = "pending",   // activated by the webhook on payment success; voided on failure
            };
            card.Id = await _giftCards.Create(card);

            // Apply rider service charge (same rule as other purchases — 100% of tenant
            // service charge bps, no per-product override for gift cards).
            var serviceCharge = (int)((long)request.AmountCents * tenant.ServiceChargeBps / 10_000L);
            var totalToCharge = request.AmountCents + serviceCharge;

            var metadata = new Dictionary<string, string>
            {
                ["tenant_id"] = _tenantContext.TenantId.ToString(),
                ["gift_card_id"] = card.Id.ToString(),
                ["sale_kind"] = "gift_card",
                ["user_id"] = userId.ToString(),
            };

            // Direct-charge tenants sell the gift card on their own account; our service fee rides as
            // the application fee. The face value lands in the tenant's account (their liability to
            // honor on redemption). The connected account is snapshotted below so the reconciler can
            // check the PI status on the right account if the webhook is missed.
            var chargePlan = _chargeRouter.Plan(tenant, serviceCharge, totalToCharge);
            PaymentIntentCreated intent;
            try
            {
                intent = await _payments.CreatePaymentIntentAsync(
                    amountCents: totalToCharge,
                    currency: "usd",
                    metadata: metadata,
                    receiptEmail: buyer.Email,
                    connectedAccountId: chargePlan.ConnectedAccountId,
                    applicationFeeCents: chargePlan.ApplicationFeeCents,
                    ct: ct);
            }
            catch (InvalidOperationException ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }

            await _giftCards.SetStripePaymentIntentId(card.Id, intent.IntentId, chargePlan.ConnectedAccountId);

            return new ApiResponses().OkResult(new BuyGiftCardResponse
            {
                GiftCardId = card.Id,
                ClientSecret = intent.ClientSecret,
                AmountCents = totalToCharge,
            });
        }

        // Effective cap on a training group: the group's own inventory and its coach's
        // per-session limit both apply, so the real ceiling is whichever is lower. Either may be
        // absent (a coach is optional, and so is inventory); null means genuinely uncapped.
        private async Task<int?> EffectiveTierCap(Services.Repositories.Data.PaymentData.EventTicketTier tier)
        {
            int? coachCap = null;
            if (tier.InstructorId is Guid coachId)
            {
                var coach = await _instructors.Get(coachId, _tenantContext.TenantId);
                coachCap = coach?.MaxStudentsPerSession;
            }
            if (tier.Inventory is null) return coachCap;
            if (coachCap is null) return tier.Inventory;
            return Math.Min(tier.Inventory.Value, coachCap.Value);
        }

    }
}
