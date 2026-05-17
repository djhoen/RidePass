using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Coupons;
using Services.Helpers;
using Services.Payments;
using Services.Repositories.Data.CouponData;
using Services.Repositories.Data.ExtrasData;
using Services.Repositories.Data.GiftCardData;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Extras;
using webapi.Controllers.API.Data.Purchase;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PurchaseController : ControllerBase
    {
        private readonly IPassProductRepository _products;
        private readonly IPassPurchaseRepository _purchases;
        private readonly IWaiverRepository _waivers;
        private readonly IUserRepository _users;
        private readonly IEventRepository _events;
        private readonly IEventTicketTierRepository _tiers;
        private readonly IEventTicketPurchaseRepository _ticketPurchases;
        private readonly IDisputeRepository _disputes;
        private readonly IPaymentProvider _payments;
        private readonly IRewardRepository _rewards;
        private readonly ITenantLedgerRepository _ledger;
        private readonly ICouponRepository _coupons;
        private readonly ICouponValidator _couponValidator;
        private readonly IGiftCardRepository _giftCards;
        private readonly Services.GiftCards.IGiftCardValidator _giftCardValidator;
        private readonly Services.Waitlist.IWaitlistPromoter _waitlistPromoter;
        private readonly IEventExtraRepository _extras;
        private readonly IMembershipRepository _memberships;
        private readonly IRecentSalesRepository _recentSales;
        private readonly ITenantContext _tenantContext;

        public PurchaseController(
            IPassProductRepository products,
            IPassPurchaseRepository purchases,
            IWaiverRepository waivers,
            IUserRepository users,
            IEventRepository events,
            IEventTicketTierRepository tiers,
            IEventTicketPurchaseRepository ticketPurchases,
            IDisputeRepository disputes,
            IPaymentProvider payments,
            IRewardRepository rewards,
            ITenantLedgerRepository ledger,
            ICouponRepository coupons,
            ICouponValidator couponValidator,
            IGiftCardRepository giftCards,
            Services.GiftCards.IGiftCardValidator giftCardValidator,
            Services.Waitlist.IWaitlistPromoter waitlistPromoter,
            IEventExtraRepository extras,
            IMembershipRepository memberships,
            IRecentSalesRepository recentSales,
            ITenantContext tenantContext)
        {
            _products = products;
            _purchases = purchases;
            _waivers = waivers;
            _users = users;
            _events = events;
            _tiers = tiers;
            _ticketPurchases = ticketPurchases;
            _disputes = disputes;
            _payments = payments;
            _rewards = rewards;
            _ledger = ledger;
            _coupons = coupons;
            _couponValidator = couponValidator;
            _giftCards = giftCards;
            _giftCardValidator = giftCardValidator;
            _waitlistPromoter = waitlistPromoter;
            _extras = extras;
            _memberships = memberships;
            _recentSales = recentSales;
            _tenantContext = tenantContext;
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

        // Centralised membership gate. Returns null when allowed, or an error message
        // ready to surface as a 400. Friendly enough that the rider can recognise it
        // and pivot to the /Membership page.
        private async Task<string?> CheckMembershipGate(Guid? userId, bool gateOn)
        {
            if (!gateOn) return null;
            var tenant = _tenantContext.Tenant;
            if (!tenant.MembershipEnabled || tenant.MembershipPriceCents <= 0) return null;
            if (!userId.HasValue)
            {
                return $"Participants are required to have a {tenant.MembershipName} — please sign in.";
            }
            var active = await _memberships.GetActive(userId.Value, tenant.Id, DateTime.UtcNow);
            if (active is null)
            {
                return $"Participants are required to have an active {tenant.MembershipName}. ";
            }
            return null;
        }

        [Authorize]
        [HttpPost("Pass")]
        public async Task<IActionResult> BuyPass([FromBody] CreatePurchaseRequest request, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved)
            {
                return new ApiResponses().BadRequestResult("No tenant resolved.");
            }

            if (!TryGetUserId(out var userId))
            {
                return new ApiResponses().BadRequestResult("Invalid token.");
            }

            var user = await _users.GetById(userId);
            // Riders are global (tenant_id null) and may buy at any tenant. Tenant-scoped users
            // (tenant_admin/tenant_staff) are pinned to their home tenant.
            if (user is null || (user.TenantId.HasValue && user.TenantId != _tenantContext.TenantId))
            {
                return new ApiResponses().BadRequestResult("User not found in this tenant.");
            }

            var product = await _products.GetById(request.ProductId, _tenantContext.TenantId);
            if (product is null || !product.IsActive)
            {
                return new ApiResponses().BadRequestResult("Product is not available.");
            }

            var quantity = Math.Max(1, request.Quantity);
            var tenant = _tenantContext.Tenant;

            // Membership gate (configured per tenant). Day passes are rider-bound, so
            // the rider audience flag governs. When the rider opts to bundle the
            // membership into this same checkout (addMembership=true) we skip the
            // gate and create the membership purchase below alongside the pass row.
            var bundleMembership = false;
            if (request.AddMembership && tenant.MembershipEnabled && tenant.MembershipPriceCents > 0)
            {
                var existing = await _memberships.GetActive(userId, tenant.Id, DateTime.UtcNow);
                bundleMembership = existing is null;
            }
            if (!bundleMembership)
            {
                var passGateError = await CheckMembershipGate(userId, tenant.MembershipRequiredForRiders);
                if (passGateError is not null) return new ApiResponses().BadRequestResult(passGateError);
            }

            if (tenant.RequireEmergencyContact && string.IsNullOrWhiteSpace(user.EmergencyContactPhone))
            {
                return new ApiResponses().BadRequestResult("Please add an emergency contact on your profile before purchasing.");
            }

            // Day-pass purchases are always tied to an event now — riders pick the
            // event first, see which products are eligible, then buy. Standalone
            // (eventId=null) purchases are no longer accepted.
            Guid? eventId = request.EventId;
            if (!eventId.HasValue)
            {
                return new ApiResponses().BadRequestResult("Day passes must be tied to an event — pick one from the calendar first.");
            }

            DateTime? validOnDate = request.ValidOnDate?.Date;
            bool eventRequiresWaiver = false;

            if (eventId.HasValue)
            {
                var ev = await _events.GetById(eventId.Value, _tenantContext.TenantId);
                if (ev is null || ev.Status != "scheduled")
                {
                    return new ApiResponses().BadRequestResult("Selected event is not available.");
                }
                if (ev.EndsAt < DateTime.UtcNow)
                {
                    return new ApiResponses().BadRequestResult("That event has already ended.");
                }
                if (!ev.Capacity.HasValue)
                {
                    return new ApiResponses().BadRequestResult("Selected event is not reservable (no capacity set).");
                }

                // Eligibility gate — selected product must be in the event's allow-list.
                var eligible = await _events.IsPassProductEligible(eventId.Value, product.Id);
                if (!eligible)
                {
                    return new ApiResponses().BadRequestResult(
                        $"\"{product.Name}\" isn't accepted at this event. Pick an eligible pass.");
                }

                var reserved = await _purchases.ActiveSpotsReservedForEvent(eventId.Value);
                var remaining = ev.Capacity.Value - reserved;
                if (quantity > remaining)
                {
                    return new ApiResponses().BadRequestResult(
                        remaining <= 0
                            ? "This event is sold out."
                            : $"Only {remaining} spot{(remaining == 1 ? string.Empty : "s")} left; requested {quantity}.");
                }

                // Auto-set valid_on_date to the event's start date.
                validOnDate = ev.StartsAt.Date;
                // Day-pass purchases are rider-audience.
                eventRequiresWaiver = ev.RequiresRiderWaiver;
            }

            // Pre-validate extras cart so we can run waiver gating once for the combined order.
            // Dedupe by (product, variant), sum quantities; reject zero-qty entries.
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
                if (!tenant.ExtrasEnabled)
                {
                    return new ApiResponses().BadRequestResult("Add-ons are not enabled at this track.");
                }
                // Per-extras membership gate (audience = spectators). Bundled-membership
                // purchases satisfy the gate via the row we'll create below.
                if (!bundleMembership)
                {
                    var extrasGateError = await CheckMembershipGate(userId, tenant.MembershipRequiredForSpectators);
                    if (extrasGateError is not null) return new ApiResponses().BadRequestResult(extrasGateError);
                }

                foreach (var item in extrasItems)
                {
                    var ep = await _extras.GetProduct(item.ProductId, _tenantContext.TenantId);
                    if (ep is null || !ep.IsActive)
                    {
                        return new ApiResponses().BadRequestResult("One of the selected add-ons isn't available.");
                    }
                    var elig = await _extras.GetEligibility(eventId.Value, ep.Id);
                    if (elig is null)
                    {
                        return new ApiResponses().BadRequestResult($"\"{ep.Name}\" isn't offered at this event.");
                    }

                    var resolved = await ResolveExtraVariant(ep, item, elig);
                    if (resolved.Error is not null) return new ApiResponses().BadRequestResult(resolved.Error);

                    if (ep.RequiresWaiver) extrasNeedWaiver = true;

                    var unitPriceFrozen = resolved.UnitPriceCents;
                    var serviceChargePerUnit = (int)((long)unitPriceFrozen * tenant.ServiceChargeBps / 10_000L);
                    var riderPortionPerUnit = (int)((long)serviceChargePerUnit * ep.RiderPaidServiceChargeBps / 10_000L);
                    var unitAmount = unitPriceFrozen + riderPortionPerUnit;
                    extrasLines.Add((ep, resolved.Variant, item.Quantity, unitAmount, serviceChargePerUnit, unitPriceFrozen));
                    extrasTotalCents += unitAmount * item.Quantity;
                    extrasServiceChargeCents += serviceChargePerUnit * item.Quantity;
                }
            }

            // Enforce waiver if the pass, the event, OR any selected add-on opts in.
            Guid? signatureId = null;
            if (product.RequiresWaiver || eventRequiresWaiver || extrasNeedWaiver)
            {
                var activeWaiver = await _waivers.GetActive(_tenantContext.TenantId);
                if (activeWaiver is not null)
                {
                    var sig = await _waivers.GetSignature(userId, activeWaiver.Id);
                    if (sig is null)
                    {
                        return new ApiResponses().BadRequestResult("Rider must sign the current waiver before purchasing.");
                    }
                    signatureId = sig.Id;
                }
            }

            // Voucher: if applied, must be on a single-pass purchase (quantity=1) so we don't have
            // to split rows. Server validates ownership, redemption status, and program scope.
            int effectiveUnitPrice = product.PriceCents;
            if (request.RewardRedemptionId.HasValue)
            {
                if (quantity != 1)
                {
                    return new ApiResponses().BadRequestResult("Vouchers can only be applied to single-pass purchases — buy them one at a time to use a reward.");
                }
                var voucherCheck = await ValidateVoucher(request.RewardRedemptionId.Value, userId, "pass");
                if (voucherCheck.error is not null) return new ApiResponses().BadRequestResult(voucherCheck.error);
                effectiveUnitPrice = product.PriceCents - (product.PriceCents * voucherCheck.percentOff!.Value / 100);
            }

            // Coupon discount applies to the whole line (price * quantity) before service charge.
            // Mutually exclusive with reward voucher.
            CouponApplication? dpCoupon = null;
            if (!string.IsNullOrWhiteSpace(request.CouponCode))
            {
                if (request.RewardRedemptionId.HasValue)
                    return new ApiResponses().BadRequestResult("You can use either a reward voucher or a coupon, not both.");
                var subtotal = effectiveUnitPrice * quantity;
                var v = await _couponValidator.ValidateAsync(_tenantContext.TenantId, request.CouponCode!,
                    scope: "pass", eventId: eventId, subtotalCents: subtotal, userId: userId);
                if (v.error is not null) return new ApiResponses().BadRequestResult(v.error);
                dpCoupon = v.application;
                // Distribute discount evenly across the unit price (last unit absorbs rounding).
                var perUnit = dpCoupon!.DiscountCents / quantity;
                effectiveUnitPrice -= perUnit;
            }

            var (amountCents, serviceChargeCents) = ComputeWithServiceCharge(
                effectiveUnitPrice, quantity, tenant.ServiceChargeBps, product.RiderPaidServiceChargeBps);

            var purchase = new PassPurchase
            {
                TenantId = _tenantContext.TenantId,
                PurchaserUserId = userId,
                ProductId = product.Id,
                WaiverSignatureId = signatureId,
                ValidOnDate = validOnDate,
                EventId = eventId,
                Quantity = quantity,
                AmountCents = amountCents,
                ServiceChargeCents = serviceChargeCents,
                AppliedRewardRedemptionId = request.RewardRedemptionId,
                PaymentMethod = amountCents == 0 ? "voucher" : "stripe",
                Status = "pending",
                PurchaserEmail = user.Email,
                PurchaserName = $"{user.FirstName} {user.LastName}".Trim(),
            };
            var createdDay = await _purchases.Create(purchase);
            purchase.Id = createdDay.Id;
            purchase.RedemptionToken = createdDay.RedemptionToken;

            if (dpCoupon is not null)
            {
                await _coupons.RecordRedemption(new CouponRedemption
                {
                    CouponId = dpCoupon.Coupon.Id,
                    TenantId = _tenantContext.TenantId,
                    UserId = userId,
                    SourceKind = "pass",
                    SourceId = purchase.Id,
                    DiscountCents = dpCoupon.DiscountCents,
                });
            }

            // Gift card: applied AFTER discounts as a payment instrument.
            GiftCardApplication? dpGift = null;
            if (!string.IsNullOrWhiteSpace(request.GiftCardCode) && amountCents > 0)
            {
                var gcCheck = await _giftCardValidator.ResolveAsync(_tenantContext.TenantId,
                    request.GiftCardCode!, amountCents);
                if (gcCheck.error is not null) return new ApiResponses().BadRequestResult(gcCheck.error);
                dpGift = gcCheck.application;
                await _giftCards.RecordRedemption(new GiftCardRedemption
                {
                    GiftCardId = dpGift!.Card.Id,
                    TenantId = _tenantContext.TenantId,
                    UserId = userId,
                    SourceKind = "pass",
                    SourceId = purchase.Id,
                    AmountCents = dpGift.AmountToApplyCents,
                });
                await _giftCards.ApplyToBalance(dpGift.Card.Id, dpGift.AmountToApplyCents);
            }
            var dpStripeChargeCents = amountCents - (dpGift?.AmountToApplyCents ?? 0);

            // Persist extras rows (one per unit so each gets its own QR). They share
            // the pass's PaymentIntent so the webhook flips them all together. Same
            // pattern the standalone Extra/Buy endpoint uses. Variant attrs frozen.
            var extraPurchaseIds = new List<Guid>();
            foreach (var line in extrasLines)
            {
                for (int q = 0; q < line.Quantity; q++)
                {
                    var ep = new EventExtraPurchase
                    {
                        TenantId = _tenantContext.TenantId,
                        EventId = eventId.Value,
                        ProductId = line.Product.Id,
                        PurchaserUserId = userId,
                        PurchaserEmail = user.Email,
                        PurchaserName = $"{user.FirstName} {user.LastName}".Trim(),
                        WaiverSignatureId = line.Product.RequiresWaiver ? signatureId : null,
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

            // Build the membership purchase up front so its price counts toward the
            // combined PI. The row gets stamped with the PI id below alongside the
            // pass / extras rows so the webhook flips it on payment_intent.succeeded.
            Guid? bundledMembershipPurchaseId = null;
            int membershipChargeCents = 0;
            if (bundleMembership)
            {
                var nowUtc = DateTime.UtcNow;
                DateTime? validTo = tenant.MembershipDurationKind == "yearly" ? nowUtc.AddDays(365) : (DateTime?)null;
                var membershipServiceCharge = (int)((long)tenant.MembershipPriceCents * tenant.ServiceChargeBps / 10_000L);
                var membership = new Services.Repositories.Data.MembershipData.MembershipPurchase
                {
                    TenantId = tenant.Id,
                    UserId = userId,
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

            var combinedStripeChargeCents = dpStripeChargeCents + extrasTotalCents + membershipChargeCents;

            // Free-order fast path: no Stripe involvement. Mark paid, write a $0 ledger row,
            // mark the redemption used inline. Only kicks in when both the pass net amount
            // AND the extras total are zero; otherwise we still need a PI for the extras.
            if (combinedStripeChargeCents == 0)
            {
                await _purchases.UpdateStatus(purchase.Id, "paid");
                await InsertZeroLedger(_tenantContext.TenantId, "pass", purchase.Id);
                foreach (var exId in extraPurchaseIds)
                {
                    await _extras.UpdateStatus(exId, "paid");
                }
                if (request.RewardRedemptionId.HasValue)
                {
                    await _rewards.MarkRedemptionUsed(request.RewardRedemptionId.Value, "pass", purchase.Id);
                }
                return new ApiResponses().OkResult(new CreatePurchaseResponse
                {
                    PurchaseId = purchase.Id,
                    RedemptionToken = purchase.RedemptionToken,
                    ClientSecret = string.Empty,
                    AmountCents = 0,
                    RiderServiceChargeCents = 0,
                    GiftCardAppliedCents = dpGift?.AmountToApplyCents ?? 0,
                });
            }

            var metadata = new Dictionary<string, string>
            {
                ["tenant_id"] = _tenantContext.TenantId.ToString(),
                ["purchase_id"] = purchase.Id.ToString(),
                ["user_id"] = userId.ToString(),
                ["product_id"] = product.Id.ToString(),
                ["quantity"] = quantity.ToString(),
            };
            if (eventId.HasValue)
            {
                metadata["event_id"] = eventId.Value.ToString();
            }
            if (dpGift is not null)
            {
                metadata["gift_card_applied_cents"] = dpGift.AmountToApplyCents.ToString();
                metadata["gift_card_id"] = dpGift.Card.Id.ToString();
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

            PaymentIntentCreated intent;
            try
            {
                intent = await _payments.CreatePaymentIntentAsync(
                    amountCents: combinedStripeChargeCents,
                    currency: "usd",
                    metadata: metadata,
                    receiptEmail: user.Email,
                    ct: ct);
            }
            catch (InvalidOperationException ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }

            await _purchases.SetStripePaymentIntentId(purchase.Id, intent.IntentId);
            if (bundledMembershipPurchaseId.HasValue)
            {
                await _memberships.SetStripePaymentIntentId(bundledMembershipPurchaseId.Value, intent.IntentId);
            }
            foreach (var exId in extraPurchaseIds)
            {
                await _extras.SetPaymentIntentId(exId, intent.IntentId);
            }

            return new ApiResponses().OkResult(new CreatePurchaseResponse
            {
                PurchaseId = purchase.Id,
                RedemptionToken = purchase.RedemptionToken,
                ClientSecret = intent.ClientSecret,
                AmountCents = combinedStripeChargeCents,
                RiderServiceChargeCents = (amountCents - effectiveUnitPrice * quantity) + extrasServiceChargeCents,
                GiftCardAppliedCents = dpGift?.AmountToApplyCents ?? 0,
            });
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
                if (tier.Inventory.HasValue)
                {
                    var sold = await _tiers.SoldCount(tier.Id);
                    if (sold + item.Quantity > tier.Inventory.Value)
                    {
                        var remaining = Math.Max(0, tier.Inventory.Value - sold);
                        return new ApiResponses().BadRequestResult(
                            $"Not enough '{tier.Name}' left ({remaining} remaining, you asked for {item.Quantity}).");
                    }
                }
                // Race classes are one-per-rider — both within this cart and across any
                // earlier (non-cancelled) entry for the same tier by this user/email.
                if (tier.Kind == "race_entry")
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

            // Authenticated buyers must have an emergency contact when the tenant requires one.
            if (purchaserUserId.HasValue && _tenantContext.Tenant.RequireEmergencyContact)
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
            if (!bundleMembership)
            {
                // Race-entry tier purchases are rider-audience.
                var ticketGateError = await CheckMembershipGate(purchaserUserId, _tenantContext.Tenant.MembershipRequiredForRiders);
                if (ticketGateError is not null) return new ApiResponses().BadRequestResult(ticketGateError);
            }
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
                // Per-extras membership gate (audience = spectators).
                // Bundled-membership purchases satisfy the gate via the row we'll create below.
                if (!bundleMembership)
                {
                    var extrasGateError = await CheckMembershipGate(purchaserUserId, ticketTenantForExtras.MembershipRequiredForSpectators);
                    if (extrasGateError is not null) return new ApiResponses().BadRequestResult(extrasGateError);
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

            // Waiver gate: required by the event (rider audience for race entries),
            // or by any selected add-on.
            Guid? extrasSignatureId = null;
            if (parentEvent.RequiresRiderWaiver || extrasNeedWaiver)
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

            // Voucher only applies for a single-tier-single-quantity cart — the math for
            // distributing a percent-off across multiple line items isn't worth the complexity.
            (int? percentOff, string? error) voucherCheck = (null, null);
            if (request.RewardRedemptionId.HasValue)
            {
                if (totalUnits != 1)
                {
                    return new ApiResponses().BadRequestResult("Reward vouchers can only be applied to a single ticket. Please remove the voucher or buy 1 ticket at a time.");
                }
                if (!purchaserUserId.HasValue)
                {
                    return new ApiResponses().BadRequestResult("Please sign in to use a reward voucher.");
                }
                voucherCheck = await ValidateVoucher(request.RewardRedemptionId.Value, purchaserUserId.Value, "event_ticket");
                if (voucherCheck.error is not null) return new ApiResponses().BadRequestResult(voucherCheck.error);
            }

            // Coupon is mutually exclusive with a reward voucher — UX rule, simpler accounting.
            CouponApplication? couponApp = null;
            if (!string.IsNullOrWhiteSpace(request.CouponCode))
            {
                if (request.RewardRedemptionId.HasValue)
                {
                    return new ApiResponses().BadRequestResult("You can use either a reward voucher or a coupon, not both.");
                }
                // Cart-wide subtotal for the coupon: tier price * qty summed (no service charge yet).
                var cartSubtotal = items.Sum(i => tierLookup[i.TierId].PriceCents * i.Quantity);
                var validation = await _couponValidator.ValidateAsync(
                    _tenantContext.TenantId, request.CouponCode!, scope: "event_ticket",
                    eventId: parentEvent.Id, subtotalCents: cartSubtotal, userId: purchaserUserId);
                if (validation.error is not null) return new ApiResponses().BadRequestResult(validation.error);
                couponApp = validation.application;
            }

            // Create one purchase row per unit. Each gets its own redemption token (QR);
            // a voucher (when present) applies to the single unit only.
            //
            // Coupon distribution: the validator returned a single discount for the whole cart
            // subtotal. We split it pro-rata across line items by sticker price so each ledger
            // row reflects what was actually discounted on it. Last unit absorbs rounding so the
            // sum exactly matches the validator's cap.
            var ticketTenant = _tenantContext.Tenant;
            var couponSubtotalDenom = couponApp is null ? 0 : items.Sum(i => tierLookup[i.TierId].PriceCents * i.Quantity);
            var couponRemaining = couponApp?.DiscountCents ?? 0;
            var totalUnitsRemaining = totalUnits;

            var createdTickets = new List<(EventTicketPurchase purchase, Services.Repositories.Data.PaymentData.EventTicketTier tier, int unitAmountCents, int unitServiceChargeCents, int couponDiscountCents)>();
            foreach (var item in items)
            {
                var tier = tierLookup[item.TierId];
                for (int q = 0; q < item.Quantity; q++)
                {
                    var unitPrice = tier.PriceCents;
                    if (voucherCheck.percentOff.HasValue)
                    {
                        unitPrice -= unitPrice * voucherCheck.percentOff.Value / 100;
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

                    var (unitAmount, unitServiceCharge) = ComputeWithServiceCharge(
                        unitPrice, quantity: 1, ticketTenant.ServiceChargeBps, tier.RiderPaidServiceChargeBps);

                    var purchase = new EventTicketPurchase
                    {
                        TenantId = _tenantContext.TenantId,
                        TierId = tier.Id,
                        PurchaserUserId = purchaserUserId,
                        AmountCents = unitAmount,
                        ServiceChargeCents = unitServiceCharge,
                        AppliedRewardRedemptionId = (q == 0 && voucherCheck.percentOff.HasValue) ? request.RewardRedemptionId : null,
                        PaymentMethod = unitAmount == 0 ? "voucher" : "stripe",
                        Status = "pending",
                        PurchaserEmail = purchaserEmail,
                        PurchaserName = purchaserName,
                    };
                    var created = await _ticketPurchases.Create(purchase);
                    purchase.Id = created.Id;
                    purchase.RedemptionToken = created.RedemptionToken;
                    createdTickets.Add((purchase, tier, unitAmount, unitServiceCharge, unitCouponDiscount));
                }
            }

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
                await _giftCards.ApplyToBalance(gcApp.Card.Id, gcApp.AmountToApplyCents);
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

            // Build the membership purchase up front (when bundling) so its price
            // counts toward the combined PI total. The row gets stamped with the PI
            // id below alongside ticket / extras rows so the webhook flips it on
            // payment_intent.succeeded.
            Guid? bundledMembershipPurchaseId = null;
            int membershipChargeCents = 0;
            if (bundleMembership && purchaserUserId.HasValue)
            {
                var tenant = _tenantContext.Tenant;
                var nowUtc = DateTime.UtcNow;
                DateTime? validTo = tenant.MembershipDurationKind == "yearly" ? nowUtc.AddDays(365) : (DateTime?)null;
                var membershipServiceCharge = (int)((long)tenant.MembershipPriceCents * tenant.ServiceChargeBps / 10_000L);
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

            var combinedStripeChargeCents = stripeChargeCents + extrasTotalCents + membershipChargeCents;

            // Free-cart fast path: voucher (single-item 100% off) OR gift card fully covered,
            // AND no add-on charges. Otherwise we still need a PI for the extras.
            if (combinedStripeChargeCents == 0)
            {
                foreach (var t in createdTickets)
                {
                    await _ticketPurchases.UpdateStatus(t.purchase.Id, "paid");
                    await InsertZeroLedger(_tenantContext.TenantId, "event_ticket", t.purchase.Id);
                }
                foreach (var exId in extraPurchaseIds)
                {
                    await _extras.UpdateStatus(exId, "paid");
                }
                if (request.RewardRedemptionId.HasValue)
                {
                    await _rewards.MarkRedemptionUsed(request.RewardRedemptionId.Value, "event_ticket", first.Id);
                }
                return new ApiResponses().OkResult(new CreatePurchaseResponse
                {
                    PurchaseId = first.Id,
                    RedemptionToken = first.RedemptionToken,
                    Tickets = redemptions,
                    ClientSecret = string.Empty,
                    AmountCents = 0,
                    RiderServiceChargeCents = 0,
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

            PaymentIntentCreated intent;
            try
            {
                intent = await _payments.CreatePaymentIntentAsync(
                    amountCents: combinedStripeChargeCents,
                    currency: "usd",
                    metadata: metadata,
                    receiptEmail: purchaserEmail,
                    ct: ct);
            }
            catch (InvalidOperationException ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }

            // Every ticket row points at the same PI so the webhook handler can find them all.
            foreach (var t in createdTickets)
            {
                await _ticketPurchases.SetStripePaymentIntentId(t.purchase.Id, intent.IntentId);
            }
            foreach (var exId in extraPurchaseIds)
            {
                await _extras.SetPaymentIntentId(exId, intent.IntentId);
            }
            if (bundledMembershipPurchaseId.HasValue)
            {
                await _memberships.SetStripePaymentIntentId(bundledMembershipPurchaseId.Value, intent.IntentId);
            }

            return new ApiResponses().OkResult(new CreatePurchaseResponse
            {
                PurchaseId = first.Id,
                RedemptionToken = first.RedemptionToken,
                Tickets = redemptions,
                ClientSecret = intent.ClientSecret,
                AmountCents = combinedStripeChargeCents,
                RiderServiceChargeCents = totalServiceChargeCents + extrasServiceChargeCents,
                GiftCardAppliedCents = gcApp?.AmountToApplyCents ?? 0,
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

        private async Task<(int? percentOff, string? error)> ValidateVoucher(Guid redemptionId, Guid userId, string itemKind)
        {
            var redemption = await _rewards.GetRedemption(redemptionId);
            if (redemption is null || redemption.UserId != userId)
            {
                return (null, "That voucher isn't yours.");
            }
            if (redemption.RedeemedAt is not null)
            {
                return (null, "That voucher has already been used.");
            }
            var program = await _rewards.GetProgram(redemption.ProgramId, _tenantContext.TenantId);
            if (program is null || !program.IsActive)
            {
                return (null, "That voucher's program is no longer active.");
            }
            if (program.RequirementKind != "any" && program.RequirementKind != itemKind)
            {
                return (null, $"That voucher only applies to {(program.RequirementKind == "pass" ? "passes" : "event tickets")}.");
            }
            return (program.RewardPercentOff, null);
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
                    Memo = "Free purchase via reward voucher",
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
            [FromQuery] string? status)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            // Reads from v_recent_sales (Script0080) so every sale kind shows up
            // — day passes, event tickets, gate fees, season passes, memberships,
            // gift cards, rentals. A practical cap of 500 prevents a stray query
            // from pulling years of activity.
            var rows = await _recentSales.List(_tenantContext.TenantId, fromUtc, toUtc, status, limit: 500);
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
            });
            return new ApiResponses().OkResult(response);
        }

        [Authorize(Policy = TenantPermissions.Policy.SalesCancel)]
        [HttpPost("Pass/{id:guid}/Cancel")]
        public async Task<IActionResult> CancelPass(Guid id, [FromBody] CancelPurchaseRequest request)
        {
            var existing = await _purchases.GetById(id, _tenantContext.TenantId);
            if (existing is null)
            {
                return new ApiResponses().NotFoundResult("Purchase not found.");
            }
            if (existing.Status != "paid")
            {
                return new ApiResponses().BadRequestResult($"Cannot cancel a purchase with status '{existing.Status}'.");
            }
            if (!TryGetUserId(out var adminId))
            {
                return new ApiResponses().BadRequestResult("Invalid token.");
            }

            await _purchases.Cancel(id, _tenantContext.TenantId, adminId, request.Reason);
            // Day-pass cancel frees an event-level spot — promote the next alternate
            // in the (event, no-tier) bucket. Fire-and-forget so the admin's request
            // returns immediately even if SMS is slow.
            if (existing.EventId.HasValue)
            {
                _ = _waitlistPromoter.PromoteNext(existing.EventId.Value, null);
            }
            return new ApiResponses().OkResult(new { id, status = "cancelled" });
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
            // Tier-specific promote: if there's an alternate waiting on this exact tier
            // (Pro division etc.), they get the spot first. Need the event id to scope.
            var tier = await _tiers.GetById(existing.TierId, _tenantContext.TenantId);
            if (tier is not null)
            {
                _ = _waitlistPromoter.PromoteNext(tier.EventId, existing.TierId);
            }
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
                Kind = d.PassPurchaseId.HasValue ? "pass"
                     : d.EventTicketPurchaseId.HasValue ? "event_ticket"
                     : "unlinked",
                PurchaseId = d.PassPurchaseId ?? d.EventTicketPurchaseId,
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
        // the gift_card row up front (status='active', delivery='pending') so the
        // payment intent can reference it; the webhook handler flips paid + sends
        // the email (or schedules it via the delivery worker) when Stripe confirms.

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
                Status = "active",
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

            PaymentIntentCreated intent;
            try
            {
                intent = await _payments.CreatePaymentIntentAsync(
                    amountCents: totalToCharge,
                    currency: "usd",
                    metadata: metadata,
                    receiptEmail: buyer.Email,
                    ct: ct);
            }
            catch (InvalidOperationException ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }

            await _giftCards.SetStripePaymentIntentId(card.Id, intent.IntentId);

            return new ApiResponses().OkResult(new BuyGiftCardResponse
            {
                GiftCardId = card.Id,
                ClientSecret = intent.ClientSecret,
                AmountCents = totalToCharge,
            });
        }
    }
}
