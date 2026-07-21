using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Helpers.Interfaces;
using Services.Payments;
using Services.Repositories.Data.ExtrasData;
using Services.Repositories.Data.MembershipData;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Data.UserData;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Counter;
using webapi.Helpers;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    /// <summary>
    /// In-person POS for tenant staff: look up or create a rider, build a cart of any
    /// sellable item, sign the waiver on the rider's behalf, charge a single PaymentIntent.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = TenantPermissions.Policy.SalesCounter)]
    public class CounterController : ControllerBase
    {
        private readonly IUserRepository _users;
        private readonly IWaiverRepository _waivers;
        private readonly IEventRepository _events;
        private readonly IEventTicketTierRepository _tiers;
        private readonly IEventTicketPurchaseRepository _ticketPurchases;
        private readonly IPaymentProvider _payments;
        private readonly IChargeRouter _chargeRouter;
        private readonly IPasswordHasher<User> _passwordHasher;
        private readonly IRewardRepository _rewards;
        private readonly Services.Email.IEventOrderConfirmationEmailer _orderConfirmations;
        private readonly ITenantLedgerRepository _ledger;
        private readonly IEventExtraRepository _extras;
        private readonly IBikeShopRepository _shop;
        private readonly IInstructorRepository _instructors;
        private readonly IMembershipRepository _memberships;
        private readonly IFeeCalculator _feeCalculator;
        private readonly ITenantRepository _tenants;
        private readonly IDbHelper _db;
        private readonly ITenantCreditRepository _credit;
        private readonly webapi.Payments.IStripePurchaseFinalizer _finalizer;
        private readonly Services.Rewards.IRewardEngine _rewardEngine;
        private readonly ITenantContext _tenantContext;
        private readonly ITenantTaxRepository _tax;
        private readonly IConfiguration _config;

        public CounterController(
            IUserRepository users,
            IWaiverRepository waivers,
            IEventRepository events,
            IEventTicketTierRepository tiers,
            IEventTicketPurchaseRepository ticketPurchases,
            IPaymentProvider payments,
            IChargeRouter chargeRouter,
            IPasswordHasher<User> passwordHasher,
            IRewardRepository rewards,
            Services.Email.IEventOrderConfirmationEmailer orderConfirmations,
            ITenantLedgerRepository ledger,
            IEventExtraRepository extras,
            IBikeShopRepository shop,
            IInstructorRepository instructors,
            IMembershipRepository memberships,
            IFeeCalculator feeCalculator,
            ITenantRepository tenants,
            IDbHelper db,
            ITenantContext tenantContext,
            ITenantTaxRepository tax,
            IConfiguration config,
            ITenantCreditRepository credit,
            webapi.Payments.IStripePurchaseFinalizer finalizer,
            Services.Rewards.IRewardEngine rewardEngine)
        {
            _credit = credit;
            _finalizer = finalizer;
            _rewardEngine = rewardEngine;
            _users = users;
            _waivers = waivers;
            _events = events;
            _tiers = tiers;
            _ticketPurchases = ticketPurchases;
            _payments = payments;
            _chargeRouter = chargeRouter;
            _passwordHasher = passwordHasher;
            _rewards = rewards;
            _orderConfirmations = orderConfirmations;
            _ledger = ledger;
            _extras = extras;
            _shop = shop;
            _instructors = instructors;
            _memberships = memberships;
            _feeCalculator = feeCalculator;
            _tenants = tenants;
            _db = db;
            _tenantContext = tenantContext;
            _tax = tax;
            _config = config;
        }


        // Loads the tenant's admission tax config (once per sale). None when no active, non-zero rate.
        private async Task<AdmissionTaxConfig> LoadAdmissionTaxConfig(Guid tenantId)
        {
            var row = await _tax.GetByKind(tenantId, "admission");
            return row is { IsActive: true, RateBps: > 0 }
                ? new AdmissionTaxConfig(row.RateBps, row.PricesIncludeTax, row.ServiceChargeTaxable)
                : AdmissionTaxConfig.None;
        }

        [HttpPost("Riders/Find")]
        public async Task<IActionResult> FindRider([FromBody] RiderLookupRequest request)
        {
            if (!_tenantContext.IsResolved)
            {
                return new ApiResponses().BadRequestResult("No tenant resolved.");
            }

            // Counter sale can sell to any user — global riders, this tenant's staff
            // (tenant_admin buying for themselves), or super_admins testing. Look up
            // global accounts first, then fall back to this tenant's scoped users.
            var rider = await _users.GetGlobalByEmail(request.Email.Trim())
                     ?? await _users.GetByEmail(_tenantContext.TenantId, request.Email.Trim());
            if (rider is null)
            {
                return new ApiResponses().NotFoundResult("No customer with that email.");
            }

            var activeWaiver = await _waivers.GetActive(_tenantContext.TenantId);
            bool signedCurrent = true;
            DateTime? signedAt = null;
            string? signatureDataUrl = null;
            bool signedByParent = false;
            string? parentName = null;
            string? parentPhone = null;
            if (activeWaiver is not null)
            {
                var sig = await _waivers.GetSignature(rider.Id, activeWaiver.Id);
                signedCurrent = sig is not null;
                if (sig is not null)
                {
                    signedAt = DateTime.SpecifyKind(sig.SignedAt, DateTimeKind.Utc);
                    signatureDataUrl = sig.SignatureDataUrl;
                    signedByParent = sig.SignedByParent;
                    parentName = sig.ParentName;
                    parentPhone = sig.ParentPhone;
                }
            }

            return new ApiResponses().OkResult(new RiderLookupResponse
            {
                Id = rider.Id,
                Email = rider.Email,
                FirstName = rider.FirstName,
                LastName = rider.LastName,
                HasSignedCurrentWaiver = signedCurrent,
                WaiverSignedAtUtc = signedAt,
                WaiverSignatureDataUrl = signatureDataUrl,
                IsMinor = WaiverPolicy.IsMinor(rider.Birthdate),
                WaiverSignedByParent = signedByParent,
                WaiverParentName = parentName,
                WaiverParentPhone = parentPhone,
                EmergencyContactName = rider.EmergencyContactName,
                EmergencyContactPhone = rider.EmergencyContactPhone,
            });
        }

        [HttpPost("Riders")]
        public async Task<IActionResult> CreateRider([FromBody] CreateCounterRiderRequest request)
        {
            if (!_tenantContext.IsResolved)
            {
                return new ApiResponses().BadRequestResult("No tenant resolved.");
            }

            var email = request.Email.Trim();
            // Mirror the lookup probe so we don't try to create a duplicate when an existing
            // tenant-scoped account would have shown up in Find.
            var existing = await _users.GetGlobalByEmail(email)
                        ?? await _users.GetByEmail(_tenantContext.TenantId, email);
            if (existing is not null)
            {
                return new ApiResponses().BadRequestResult("A customer with that email already exists — use Find instead.");
            }
            if (!UserController.IsValidBirthdate(request.Birthdate))
            {
                return new ApiResponses().BadRequestResult("Please enter a valid birthdate.");
            }
            var contactName = request.EmergencyContactName.Trim();
            var contactPhone = request.EmergencyContactPhone.Trim();
            if (contactName.Length == 0 || UserController.DigitsOnly(contactPhone).Length < 7)
            {
                return new ApiResponses().BadRequestResult("Please enter a valid emergency contact name and phone number.");
            }

            // Random unguessable password the rider never sees. They claim the account later via reset.
            Span<byte> bytes = stackalloc byte[32];
            RandomNumberGenerator.Fill(bytes);
            var unknownPassword = Convert.ToHexString(bytes);

            var rider = new User
            {
                TenantId = null,         // Riders are global — one account works at every tenant.
                Email = email,
                FirstName = request.FirstName.Trim(),
                LastName = request.LastName.Trim(),
                Role = "rider",
                Status = "active",
                Birthdate = request.Birthdate.Date,
                EmergencyContactName = contactName,
                EmergencyContactPhone = contactPhone,
            };
            rider.PasswordHash = _passwordHasher.HashPassword(rider, unknownPassword);
            rider.Id = await _users.Create(rider);

            return new ApiResponses().OkResult(new CreateCounterRiderResponse
            {
                Id = rider.Id,
                Email = rider.Email,
                FirstName = rider.FirstName,
                LastName = rider.LastName,
            });
        }

        [HttpPost("Sale")]
        public async Task<IActionResult> CreateSale([FromBody] CounterSaleRequest request, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved)
            {
                return new ApiResponses().BadRequestResult("No tenant resolved.");
            }

            var requestedMethod = string.IsNullOrWhiteSpace(request.PaymentMethod) ? "stripe" : request.PaymentMethod;
            if (requestedMethod is not ("stripe" or "cash" or "card_present"))
            {
                return new ApiResponses().BadRequestResult("paymentMethod must be 'stripe', 'cash', or 'card_present'.");
            }
            // Card-present (Tap to Pay) is a Stripe charge for storage and refund purposes; only
            // the PaymentIntent shape differs. So treat it as 'stripe' everywhere downstream
            // (purchase rows, refund routing) and branch on cardPresent only at PI creation.
            var cardPresent = requestedMethod == "card_present";
            var paymentMethod = cardPresent ? "stripe" : requestedMethod;

            // Cashier id from the JWT — stamped on every purchase row so admins
            // can audit who rang up the sale at the counter.
            Guid? cashierId = Guid.TryParse(User.FindFirst("UserId")?.Value, out var cid) ? cid : (Guid?)null;

            var rider = await _users.GetById(request.RiderId);
            if (rider is null)
            {
                return new ApiResponses().BadRequestResult("Customer not found.");
            }
            if (_tenantContext.Tenant.RequireEmergencyContact && string.IsNullOrWhiteSpace(rider.EmergencyContactPhone))
            {
                return new ApiResponses().BadRequestResult("This tenant requires an emergency contact on file. Please update the rider's profile before completing the sale.");
            }

            // Validate every cart item up front and compute total before writing anything.
            // We also collect whether any item requires the waiver — purely tenant-active-waiver
            // alone is no longer enough; per-item flags govern.
            var ticketItems = new List<(CounterCartItem Item, EventTicketTier Tier, int UnitAmountCents, int UnitServiceChargeCents, int UnitTaxCents)>();
            var extrasItems = new List<(CounterCartItem Item, EventExtraProduct Product, EventExtraVariant? Variant,
                                        int UnitAmountCents, int UnitServiceChargeCents, int UnitPriceFrozen)>();
            // Lesson bike rentals booked at the counter, on the shop catalog (shop_rental). The
            // fee is all-in and is the sale gross; the deposit is recorded on the rental but NOT
            // charged here (same as a cash booking at the bike shop counter: staff handle any
            // deposit physically, and card deposits are only held on the online flow).
            var rentalItems = new List<(Services.Repositories.Data.BikeShopData.LessonRentableInfo Bike, Guid EventId,
                                        DateTime StartsAtUtc, DateTime EndsAtUtc,
                                        int Quantity, int FrozenRate, int FeeCents, int CashCutCents,
                                        int DepositCents, List<Guid> PickedUnits)>();
            // Memberships: at most one per cart (every rider has exactly one active membership at a time).
            (CounterCartItem Item, int PriceCents, int ServiceChargeCents)? membershipItem = null;
            int totalCents = 0;
            int ticketTaxCents = 0;   // admission tax contained in totalCents (for the response)
            bool waiverRequiredByCart = false;
            var tenant = _tenantContext.Tenant;
            var admissionTax = await LoadAdmissionTaxConfig(_tenantContext.TenantId);
            foreach (var item in request.Items)
            {
                if (item.Kind == "event_ticket")
                {
                    var tier = await _tiers.GetById(item.ItemId, _tenantContext.TenantId);
                    if (tier is null || !tier.IsActive)
                    {
                        return new ApiResponses().BadRequestResult($"Ticket tier {item.ItemId} is not available.");
                    }
                    var ev = await _events.GetById(tier.EventId, _tenantContext.TenantId);
                    if (ev is null || ev.Status != "scheduled" || ev.EndsAt < DateTime.UtcNow)
                    {
                        return new ApiResponses().BadRequestResult($"Tier '{tier.Name}' is for an event that has already ended.");
                    }
                    // Price ladder: a step carries no per-step inventory. The live price is the
                    // active (highest-priced fired) step and the whole class sells against
                    // event.capacity. Resolve so the counter charges the same price an online
                    // buyer would and can't oversell the class; standalone tiers use their own
                    // inventory as before.
                    List<EventTicketTier>? ladderSteps = null;
                    if (tier.LadderGroup is not null)
                    {
                        ladderSteps = (await _tiers.GetForEvent(tier.EventId, _tenantContext.TenantId, activeOnly: true))
                            .Where(t => t.LadderGroup == tier.LadderGroup).ToList();
                        var groupSold = await _tiers.GroupSoldCount(tier.EventId, tier.LadderGroup, _tenantContext.TenantId);
                        var state = Services.Pricing.PriceStepResolver.Resolve(
                            ladderSteps, groupSold, ev.StartsAt, DateTime.UtcNow);
                        if (state is null)
                        {
                            return new ApiResponses().BadRequestResult($"Tier '{tier.Name}' isn't available right now.");
                        }
                        tier = state.Active;
                        // Capacity is not checked per ladder group: event.capacity is enforced
                        // event-wide (rider admissions across every tier) under the lock below.
                    }
                    else if (await EffectiveTierCap(tier) is int cartCap)
                    {
                        var sold = await _tiers.SoldCount(tier.Id);
                        if (sold + item.Quantity > cartCap)
                        {
                            return new ApiResponses().BadRequestResult($"Tier '{tier.Name}' has only {Math.Max(0, cartCap - sold)} left.");
                        }
                    }
                    // Race classes are one-per-rider: block duplicates within the cart and any
                    // earlier active entry for the same rider in this class. For a ladder the
                    // class spans every step, so check them all (in-cart lines collapse because
                    // each one normalized to the active step above).
                    if (tier.Kind == "race_entry")
                    {
                        if (item.Quantity > 1)
                        {
                            return new ApiResponses().BadRequestResult(
                                $"Riders can only enter '{tier.Name}' once.");
                        }
                        if (ticketItems.Any(t => t.Tier.Id == tier.Id))
                        {
                            return new ApiResponses().BadRequestResult(
                                $"Riders can only enter '{tier.Name}' once.");
                        }
                        var classStepIds = ladderSteps?.Select(s => s.Id).ToList() ?? new List<Guid> { tier.Id };
                        foreach (var stepId in classStepIds)
                        {
                            var already = await _ticketPurchases.HasActiveRaceEntry(
                                _tenantContext.TenantId, stepId, rider.Id, rider.Email);
                            if (already)
                            {
                                return new ApiResponses().BadRequestResult(
                                    $"{rider.FirstName} is already entered in '{tier.Name}'.");
                            }
                        }
                    }
                    if (tier.PartySizeMax is int cPartyMax && item.Quantity > cPartyMax)
                    {
                        return new ApiResponses().BadRequestResult(
                            $"'{tier.Name}' covers up to {cPartyMax} rider(s) per booking.");
                    }
                    // Party pricing makes riders in one booking cost different amounts, so a
                    // single unit price times quantity no longer works. Split the line into one
                    // entry per rider, each carrying its own price; the creation loop below
                    // already emits one ticket row per entry, so nothing downstream changes.
                    // An ordinary tier keeps its single entry and behaves exactly as before.
                    if (Services.Pricing.PartyPricing.IsPartyPriced(tier) && item.Quantity > 1)
                    {
                        for (var pi = 0; pi < item.Quantity; pi++)
                        {
                            var partyUnitPrice = Services.Pricing.PartyPricing.UnitPriceCents(tier, pi);
                            var (pPreTax, pServiceCharge) = ComputeWithServiceCharge(
                                partyUnitPrice, quantity: 1, tenant.ServiceChargeBps, tier.RiderPaidServiceChargeBps);
                            var pTax = AdmissionTax.Compute(partyUnitPrice, pPreTax - partyUnitPrice, admissionTax);
                            var oneRider = new CounterCartItem
                            {
                                Kind = item.Kind, ItemId = item.ItemId, Quantity = 1,
                                EventId = item.EventId, VariantId = item.VariantId,
                            };
                            ticketItems.Add((oneRider, tier, pTax.AmountToChargeCents, pServiceCharge, pTax.TaxCents));
                            totalCents += pTax.AmountToChargeCents;
                            ticketTaxCents += pTax.TaxCents;
                        }
                        if (ev.RequiresRiderWaiver) waiverRequiredByCart = true;
                        continue;
                    }
                    var (unitPreTax, unitServiceCharge) = ComputeWithServiceCharge(
                        tier.PriceCents, quantity: 1, tenant.ServiceChargeBps, tier.RiderPaidServiceChargeBps);
                    var tktTax = AdmissionTax.Compute(tier.PriceCents, unitPreTax - tier.PriceCents, admissionTax);
                    ticketItems.Add((item, tier, tktTax.AmountToChargeCents, unitServiceCharge, tktTax.TaxCents));
                    totalCents += tktTax.AmountToChargeCents * item.Quantity;
                    ticketTaxCents += tktTax.TaxCents * item.Quantity;
                    // Counter sales of race-entry tiers are rider-audience.
                    if (ev.RequiresRiderWaiver) waiverRequiredByCart = true;
                }
                else if (item.Kind == "extras")
                {
                    if (!tenant.ExtrasEnabled)
                    {
                        return new ApiResponses().BadRequestResult("Add-ons are not enabled at this track.");
                    }
                    // Counter sells add-ons as merchandise — no event attachment, no
                    // per-event eligibility check. Variant inventory still enforced
                    // because variants are tenant-wide (not per-event).
                    var product = await _extras.GetProduct(item.ItemId, _tenantContext.TenantId);
                    if (product is null || !product.IsActive)
                    {
                        return new ApiResponses().BadRequestResult("Add-on isn't available.");
                    }
                    if (product.ExpiresAt.HasValue && product.ExpiresAt.Value <= DateTime.UtcNow)
                    {
                        return new ApiResponses().BadRequestResult($"\"{product.Name}\" is no longer being sold.");
                    }
                    if (product.Inventory.HasValue)
                    {
                        var soldProduct = await _extras.SumSoldProduct(product.Id);
                        var remainingProduct = product.Inventory.Value - soldProduct;
                        if (item.Quantity > remainingProduct)
                        {
                            return new ApiResponses().BadRequestResult(remainingProduct <= 0
                                ? $"\"{product.Name}\" is sold out."
                                : $"Only {remainingProduct} of \"{product.Name}\" left.");
                        }
                    }
                    EventExtraVariant? variant = null;
                    int unitPriceFrozen = product.PriceCents;
                    var variants = await _extras.ListVariants(product.Id);
                    var activeVariants = variants.Where(v => v.IsActive).ToList();
                    if (activeVariants.Count > 0)
                    {
                        if (!item.VariantId.HasValue)
                        {
                            return new ApiResponses().BadRequestResult($"Pick a variant for \"{product.Name}\".");
                        }
                        variant = activeVariants.FirstOrDefault(v => v.Id == item.VariantId.Value);
                        if (variant is null)
                        {
                            return new ApiResponses().BadRequestResult($"That option isn't available for \"{product.Name}\".");
                        }
                        if (variant.Inventory.HasValue)
                        {
                            var sold = await _extras.SumSoldVariant(variant.Id);
                            if (sold + item.Quantity > variant.Inventory.Value)
                            {
                                return new ApiResponses().BadRequestResult(
                                    $"Only {variant.Inventory.Value - sold} of that variant left.");
                            }
                        }
                        unitPriceFrozen = variant.PriceCents ?? product.PriceCents;
                    }
                    var (unitAmount, unitServiceCharge) = ComputeWithServiceCharge(
                        unitPriceFrozen, quantity: 1, tenant.ServiceChargeBps, product.RiderPaidServiceChargeBps);
                    extrasItems.Add((item, product, variant, unitAmount, unitServiceCharge, unitPriceFrozen));
                    totalCents += unitAmount * item.Quantity;
                    if (product.RequiresWaiver) waiverRequiredByCart = true;
                }
                else if (item.Kind == "rental")
                {
                    // A bike booked for a lesson at the counter. EventId is the lesson; ItemId the
                    // shop variant.
                    if (!tenant.BikeShopEnabled)
                        return new ApiResponses().BadRequestResult("Bike rentals aren't enabled at this track.");
                    if (item.EventId is not Guid lessonId)
                        return new ApiResponses().BadRequestResult("A bike rental needs a lesson to attach to.");

                    var lesson = await _events.GetById(lessonId, _tenantContext.TenantId);
                    if (lesson is null || lesson.Status != "scheduled" || lesson.EndsAt < DateTime.UtcNow)
                        return new ApiResponses().BadRequestResult("That lesson has already ended or isn't available.");
                    var bike = await _shop.GetLessonRentable(lessonId, item.ItemId, _tenantContext.TenantId);
                    if (bike is null)
                        return new ApiResponses().BadRequestResult("That bike isn't offered with this lesson.");
                    if (!bike.IsActive || (bike.PriceCentsOverride ?? bike.DailyRateCents) is null)
                        return new ApiResponses().BadRequestResult("That bike isn't available.");

                    var rStartsAt = DateTime.SpecifyKind(lesson.StartsAt, DateTimeKind.Utc);
                    var rEndsAt = DateTime.SpecifyKind(lesson.EndsAt, DateTimeKind.Utc);
                    var rQty = Math.Max(1, item.Quantity);

                    // Availability for the lesson window; pick the specific units for serialized bikes.
                    var pickedUnits = new List<Guid>();
                    if (bike.TrackingKind == "pool")
                    {
                        var rem = await _shop.GetPoolAvailability(bike.VariantId, _tenantContext.TenantId, rStartsAt, rEndsAt);
                        if (rQty > rem)
                            return new ApiResponses().BadRequestResult(
                                rem <= 0 ? "That bike is fully booked for this lesson."
                                         : $"Only {rem} of that bike available for this lesson.");
                    }
                    else
                    {
                        var free = await _shop.GetFreeSerializedUnits(bike.VariantId, _tenantContext.TenantId, rStartsAt, rEndsAt);
                        if (free.Count < rQty)
                            return new ApiResponses().BadRequestResult($"Only {free.Count} of that bike available for this lesson.");
                        pickedUnits = free.Take(rQty).Select(u => u.Id).ToList();
                    }

                    // All-in pricing (no rider service charge). Cash sales owe the platform its
                    // percentage cut, mirroring a cash booking at the bike shop counter.
                    var blockPrice = (bike.PriceCentsOverride ?? bike.DailyRateCents)!.Value;
                    var feeCents = blockPrice * rQty;
                    var cashCut = feeCents == 0 ? 0
                        : (await _feeCalculator.Calculate(_tenantContext.TenantId, feeCents, 0, 0, DateTime.UtcNow)).RidepassCutCents;
                    rentalItems.Add((bike, lessonId, rStartsAt, rEndsAt, rQty, blockPrice,
                        feeCents, cashCut, bike.DepositCents * rQty, pickedUnits));
                    totalCents += feeCents;
                }
                else if (item.Kind == "membership")
                {
                    if (!tenant.MembershipEnabled || tenant.MembershipPriceCents <= 0)
                    {
                        return new ApiResponses().BadRequestResult("Memberships aren't sold at this track.");
                    }
                    if (item.Quantity != 1)
                    {
                        return new ApiResponses().BadRequestResult("Memberships are sold one at a time.");
                    }
                    if (membershipItem.HasValue)
                    {
                        return new ApiResponses().BadRequestResult("Only one membership per sale.");
                    }
                    var serviceCharge = (int)((long)tenant.MembershipPriceCents * tenant.ServiceChargeBps / 10_000L);
                    membershipItem = (item, tenant.MembershipPriceCents, serviceCharge);
                    totalCents += tenant.MembershipPriceCents;
                }
                else
                {
                    return new ApiResponses().BadRequestResult($"Unsupported cart item kind: {item.Kind}");
                }
            }
            if (totalCents <= 0)
            {
                return new ApiResponses().BadRequestResult("Cart total must be positive.");
            }

            // Voucher: applies to ONE unit of ONE qualifying line. Day-pass lines must be
            // quantity=1 to be eligible (we don't split rows). Tickets are 1-row-per-unit
            // already so the first ticket of the chosen line gets the discount.
            int? voucherTicketIdx = null;
            int voucherPercentOff = 0;
            if (request.RewardRedemptionId.HasValue)
            {
                var voucher = await _rewards.GetRedemption(request.RewardRedemptionId.Value);
                if (voucher is null || voucher.UserId != rider.Id)
                {
                    return new ApiResponses().BadRequestResult("That voucher isn't this rider's.");
                }
                if (voucher.RedeemedAt is not null)
                {
                    return new ApiResponses().BadRequestResult("That voucher has already been used.");
                }
                var voucherProgram = await _rewards.GetProgram(voucher.ProgramId, _tenantContext.TenantId);
                if (voucherProgram is null || !voucherProgram.IsActive)
                {
                    return new ApiResponses().BadRequestResult("That voucher's program is no longer active.");
                }
                voucherPercentOff = voucherProgram.RewardPercentOff;
                var allowsTicket = voucherProgram.RequirementKind is "event_ticket" or "any";

                if (allowsTicket && ticketItems.Count > 0)
                {
                    voucherTicketIdx = 0;
                }
                if (voucherTicketIdx is null)
                {
                    return new ApiResponses().BadRequestResult(
                        "No qualifying line for this voucher — pick a race entry or gate fee.");
                }

                // Recompute discounted line + adjust totalCents.
                if (voucherTicketIdx is int tki)
                {
                    var entry = ticketItems[tki];
                    var discountedPrice = entry.Tier.PriceCents - (entry.Tier.PriceCents * voucherPercentOff / 100);
                    var (newPreTax, newUnitSc) = ComputeWithServiceCharge(
                        discountedPrice, 1, tenant.ServiceChargeBps, entry.Tier.RiderPaidServiceChargeBps);
                    var newTax = AdmissionTax.Compute(discountedPrice, newPreTax - discountedPrice, admissionTax);
                    totalCents -= entry.UnitAmountCents;
                    totalCents += newTax.AmountToChargeCents;
                    ticketTaxCents += newTax.TaxCents - entry.UnitTaxCents;
                    // Stash the discounted unit by pre-pending a synthetic entry of qty=1 and trimming the original.
                    var trimmedItem = new CounterCartItem { Kind = entry.Item.Kind, ItemId = entry.Item.ItemId, Quantity = entry.Item.Quantity - 1 };
                    var discountedItem = new CounterCartItem { Kind = entry.Item.Kind, ItemId = entry.Item.ItemId, Quantity = 1 };
                    ticketItems.RemoveAt(tki);
                    ticketItems.Insert(0, (discountedItem, entry.Tier, newTax.AmountToChargeCents, newUnitSc, newTax.TaxCents));
                    if (trimmedItem.Quantity > 0)
                    {
                        ticketItems.Insert(1, (trimmedItem, entry.Tier, entry.UnitAmountCents, entry.UnitServiceChargeCents, entry.UnitTaxCents));
                    }
                    voucherTicketIdx = 0;   // discounted entry is now at index 0
                }
            }

            // Sign waiver on rider's behalf if any cart item requires it and they haven't already signed.
            Guid? waiverSignatureId = null;
            var activeWaiver = waiverRequiredByCart ? await _waivers.GetActive(_tenantContext.TenantId) : null;
            if (activeWaiver is not null)
            {
                var existing = await _waivers.GetSignature(rider.Id, activeWaiver.Id);
                if (existing is null)
                {
                    if (!request.SignWaiver)
                    {
                        return new ApiResponses().BadRequestResult("Rider has not signed the active waiver.");
                    }
                    if (!IsValidPngDataUrl(request.SignatureDataUrl))
                    {
                        return new ApiResponses().BadRequestResult("A handwritten signature is required to sign the waiver.");
                    }
                    var isMinor = WaiverPolicy.IsMinor(rider.Birthdate);
                    string? parentName = null;
                    string? parentPhone = null;
                    if (isMinor)
                    {
                        parentName = string.IsNullOrWhiteSpace(request.ParentName) ? null : request.ParentName!.Trim();
                        parentPhone = string.IsNullOrWhiteSpace(request.ParentPhone) ? null : request.ParentPhone!.Trim();
                        if (parentName is null || parentPhone is null || parentPhone.Length < 7)
                        {
                            return new ApiResponses().BadRequestResult("Riders under 18 need a parent or guardian's name and phone number on the waiver.");
                        }
                    }
                    var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
                    waiverSignatureId = await _waivers.Sign(_tenantContext.TenantId, rider.Id, activeWaiver.Id, ip, request.SignatureDataUrl,
                        signedByParent: isMinor, parentName: parentName, parentPhone: parentPhone);
                }
                else
                {
                    waiverSignatureId = existing.Id;
                }
            }

            var purchaserName = $"{rider.FirstName} {rider.LastName}".Trim();
            var lineItems = new List<CounterSaleLineItem>();
            // Parallel list with the per-row service charge so cash + free-voucher ledger
            // writes can use the right ridepass_cut for each row.
            var ledgerLines = new List<(string Kind, Guid PurchaseId, int Gross, int ServiceCharge)>();

            // Serialize the capacity recheck + ticket inserts per event so a counter sale
            // can't oversell a tier or double-enter a race class against a concurrent online
            // or counter sale (review item #4). A counter cart may span multiple events, so
            // lock them all in a stable (sorted) order to avoid deadlocking with another
            // multi-event cart. Same key space as the online checkout, so they contend too.
            // Released right after the inserts, before the Stripe call.
            var ticketEventIds = ticketItems.Select(t => t.Tier.EventId).Distinct().OrderBy(id => id).ToList();
            var capacityLocks = new List<IAsyncDisposable>();
            try
            {
                foreach (var evId in ticketEventIds)
                    capacityLocks.Add(await _db.AcquireAdvisoryLock($"event-capacity:{evId}"));

                // Authoritative re-check under the locks (the cart-build loop was a fast-fail).
                // Cache event capacity/title to avoid reloading per ticket row.
                var rcEventCache = new Dictionary<Guid, (int? Capacity, string Title)>();
                // Event-wide rider capacity, per event in the cart. This owns event.capacity for
                // every tier shape; it replaced a per-ladder-group check that never ran for a
                // plain tier, leaving counter sales against an at-capacity event unbounded. Rider
                // admissions only, since spectators don't consume rider capacity.
                foreach (var evGrp in ticketItems
                             .Where(t => t.Tier.Audience == "rider")
                             .GroupBy(t => t.Tier.EventId))
                {
                    if (!rcEventCache.TryGetValue(evGrp.Key, out var rcEv))
                    {
                        var loaded = await _events.GetById(evGrp.Key, _tenantContext.TenantId);
                        rcEv = (loaded?.Capacity, loaded?.Title ?? string.Empty);
                        rcEventCache[evGrp.Key] = rcEv;
                    }
                    if (!rcEv.Capacity.HasValue) continue;
                    var eventSoldNow = await _tiers.EventSoldCount(evGrp.Key, _tenantContext.TenantId);
                    var riderCartUnits = evGrp.Sum(t => t.Item.Quantity);
                    if (eventSoldNow + riderCartUnits > rcEv.Capacity.Value)
                        return new ApiResponses().BadRequestResult(
                            $"Only {Math.Max(0, rcEv.Capacity.Value - eventSoldNow)} spot(s) left for \"{rcEv.Title}\".");
                }
                foreach (var (rcItem, rcTier, _, _, _) in ticketItems)
                {
                    if (rcTier.LadderGroup is null && await EffectiveTierCap(rcTier) is int rcCap)
                    {
                        var soldNow = await _tiers.SoldCount(rcTier.Id);
                        if (soldNow + rcItem.Quantity > rcCap)
                            return new ApiResponses().BadRequestResult(
                                $"Tier '{rcTier.Name}' has only {Math.Max(0, rcTier.Inventory.Value - soldNow)} left.");
                    }
                    if (rcTier.Kind == "race_entry")
                    {
                        var alreadyNow = await _ticketPurchases.HasActiveRaceEntry(
                            _tenantContext.TenantId, rcTier.Id, rider.Id, rider.Email);
                        if (alreadyNow)
                            return new ApiResponses().BadRequestResult(
                                $"{rider.FirstName} is already entered in '{rcTier.Name}'.");
                    }
                }

            for (var tkiIdx = 0; tkiIdx < ticketItems.Count; tkiIdx++)
            {
                var (item, tier, unitAmount, unitServiceCharge, unitTax) = ticketItems[tkiIdx];
                for (int i = 0; i < item.Quantity; i++)
                {
                    // The discounted entry was placed at index 0 with Quantity=1, so the voucher
                    // applies to that single ticket only; the rest stay full price.
                    var applyVoucherHere = tkiIdx == voucherTicketIdx && i == 0;
                    var t = new EventTicketPurchase
                    {
                        TenantId = _tenantContext.TenantId,
                        TierId = tier.Id,
                        PurchaserUserId = rider.Id,
                        AmountCents = unitAmount,
                        ServiceChargeCents = unitServiceCharge,
                        TaxCents = unitTax,
                        TaxRateBps = admissionTax.RateBps,
                        TaxInclusive = admissionTax.PricesIncludeTax,
                        AppliedRewardRedemptionId = applyVoucherHere ? request.RewardRedemptionId : null,
                        PaymentMethod = paymentMethod,
                        Status = "pending",
                        PurchaserEmail = rider.Email,
                        PurchaserName = purchaserName,
                        SoldByUserId = cashierId,
                        // Link the rider's waiver signature (when they signed one) so the check-in gate
                        // and the "who has signed" report read the same signature store as online sales.
                        WaiverSignatureId = waiverSignatureId,
                    };
                    var created = await _ticketPurchases.Create(t);
                    lineItems.Add(new CounterSaleLineItem
                    {
                        Kind = "event_ticket",
                        PurchaseId = created.Id,
                        RedemptionToken = created.RedemptionToken,
                        DisplayName = tier.Name,
                        Quantity = 1,
                        UnitPriceCents = tier.PriceCents,
                        LineAmountCents = unitAmount,
                    });
                    ledgerLines.Add(("event_ticket", created.Id, unitAmount, unitServiceCharge));
                }
            }
            }
            finally
            {
                // Pending rows now hold the capacity; release before the (network) Stripe call.
                foreach (var capacityLock in capacityLocks) await capacityLock.DisposeAsync();
            }
            // Extras: one event_extra_purchase row per unit so each gets its own QR. Variant
            // attrs frozen on the row. 'extras' is a valid ledger source_kind (Script0099), so
            // cash extras flow through ledgerLines below and get a sale ledger row like everything
            // else; the Stripe path's ledger rows are written by the webhook finalizer instead.
            foreach (var (item, product, variant, unitAmount, unitServiceCharge, unitPriceFrozen) in extrasItems)
            {
                for (int i = 0; i < item.Quantity; i++)
                {
                    var ep = new EventExtraPurchase
                    {
                        TenantId = _tenantContext.TenantId,
                        EventId = item.EventId,
                        ProductId = product.Id,
                        PurchaserUserId = rider.Id,
                        PurchaserEmail = rider.Email,
                        PurchaserName = purchaserName,
                        WaiverSignatureId = product.RequiresWaiver ? waiverSignatureId : null,
                        Quantity = 1,
                        UnitPriceCentsFrozen = unitPriceFrozen,
                        AmountCents = unitAmount,
                        ServiceChargeCents = unitServiceCharge,
                        Status = "pending",
                        PaymentMethod = paymentMethod,
                        VariantId = variant?.Id,
                        SizeAtPurchase = variant?.Size,
                        ColorAtPurchase = variant?.Color,
                        GenderAtPurchase = variant?.Gender,
                        SoldByUserId = cashierId,
                    };
                    var created = await _extras.CreatePurchase(ep);
                    var attrs = variant != null
                        ? new[] { variant.Size, variant.Color, variant.Gender }
                            .Where(s => !string.IsNullOrWhiteSpace(s))
                            .ToArray()
                        : Array.Empty<string?>();
                    var displayName = attrs.Length > 0
                        ? $"{product.Name} ({string.Join(" / ", attrs)})"
                        : product.Name;
                    lineItems.Add(new CounterSaleLineItem
                    {
                        Kind = "extras",
                        PurchaseId = created.Id,
                        RedemptionToken = created.RedemptionToken,
                        DisplayName = displayName,
                        Quantity = 1,
                        UnitPriceCents = unitPriceFrozen,
                        LineAmountCents = unitAmount,
                    });
                    ledgerLines.Add(("extras", created.Id, unitAmount, unitServiceCharge));
                }
            }
            // Lesson bike rentals: one shop_rental per cart line, reserved for the lesson window
            // (window overlap holds the capacity), with serialized units assigned on the lines.
            // Ledger gross = fee only; the deposit is recorded on the rental, not charged.
            foreach (var r in rentalItems)
            {
                var bikeLabel = string.Join(" / ", new[] { r.Bike.Size, r.Bike.Color, r.Bike.Gender }
                    .Where(x => !string.IsNullOrWhiteSpace(x)));
                var rentalLines = new List<Services.Repositories.Data.BikeShopData.ShopRentalLine>();
                if (r.Bike.TrackingKind == "serialized")
                {
                    rentalLines.AddRange(r.PickedUnits.Select(uid => new Services.Repositories.Data.BikeShopData.ShopRentalLine
                    {
                        VariantId = r.Bike.VariantId, ItemId = uid, Quantity = 1,
                        NameSnapshot = r.Bike.ProductName,
                        VariantLabel = string.IsNullOrEmpty(bikeLabel) ? null : bikeLabel,
                        DailyRateCentsFrozen = r.FrozenRate, DepositCentsFrozen = r.Bike.DepositCents,
                        LineAmountCents = r.FrozenRate,
                    }));
                }
                else
                {
                    rentalLines.Add(new Services.Repositories.Data.BikeShopData.ShopRentalLine
                    {
                        VariantId = r.Bike.VariantId, Quantity = r.Quantity,
                        NameSnapshot = r.Bike.ProductName,
                        VariantLabel = string.IsNullOrEmpty(bikeLabel) ? null : bikeLabel,
                        DailyRateCentsFrozen = r.FrozenRate, DepositCentsFrozen = r.Bike.DepositCents,
                        LineAmountCents = r.FeeCents,
                    });
                }
                var (rid, rToken) = await _shop.CreateRental(new Services.Repositories.Data.BikeShopData.ShopRental
                {
                    TenantId = _tenantContext.TenantId,
                    RenterUserId = rider.Id,
                    RenterName = purchaserName,
                    RenterEmail = rider.Email,
                    WaiverSignatureId = waiverSignatureId,
                    StartsAt = r.StartsAtUtc,
                    EndsAt = r.EndsAtUtc,
                    Status = "pending",
                    AmountCents = r.FeeCents,
                    TaxCents = 0,
                    TotalCents = r.FeeCents,
                    DepositCents = r.DepositCents,
                    PaymentMethod = paymentMethod == "cash" ? "cash" : "stripe",
                    SoldByUserId = cashierId,
                    EventId = r.EventId,
                }, rentalLines);
                lineItems.Add(new CounterSaleLineItem
                {
                    Kind = "shop_rental",
                    PurchaseId = rid,
                    RedemptionToken = rToken,
                    DisplayName = $"{r.Bike.ProductName} (bike)",
                    Quantity = r.Quantity,
                    UnitPriceCents = r.FrozenRate,
                    LineAmountCents = r.FeeCents,
                });
                ledgerLines.Add(("shop_rental", rid, r.FeeCents, r.CashCutCents));
            }

            // Membership: one row per sale, frozen pricing + duration.
            if (membershipItem.HasValue)
            {
                var (item, priceCents, serviceCharge) = membershipItem.Value;
                var now = DateTime.UtcNow;
                DateTime? validTo = tenant.MembershipDurationKind == "yearly" ? now.AddDays(365) : (DateTime?)null;
                var purchase = new MembershipPurchase
                {
                    TenantId = tenant.Id,
                    UserId = rider.Id,
                    NameAtPurchase = tenant.MembershipName,
                    PriceCents = priceCents,
                    DurationKind = tenant.MembershipDurationKind,
                    ValidFromUtc = now,
                    ValidToUtc = validTo,
                    AmountCents = priceCents,
                    ServiceChargeCents = serviceCharge,
                    Status = "pending",
                    PaymentMethod = paymentMethod,
                    SoldByUserId = cashierId,
                };
                purchase.Id = await _memberships.Create(purchase);
                lineItems.Add(new CounterSaleLineItem
                {
                    Kind = "membership",
                    PurchaseId = purchase.Id,
                    RedemptionToken = Guid.Empty,        // no QR for memberships
                    DisplayName = tenant.MembershipName,
                    Quantity = 1,
                    UnitPriceCents = priceCents,
                    LineAmountCents = priceCents,
                });
                ledgerLines.Add(("membership", purchase.Id, priceCents, serviceCharge));
            }

            // ── Store credit tender (Script0195): the cashier looked the account up; verify it,
            // cap at balance + total, and debit through a tender row the whole checkout anchors
            // on. Per-line ledger entries stay untouched; one balancing entry nets the books.
            var creditApplied = 0;
            Guid? creditTenderId = null;
            if (request.CreditAccountId is not null && request.CreditCents > 0 && totalCents > 0)
            {
                var creditAccount = await _credit.GetAccount(request.CreditAccountId.Value, _tenantContext.TenantId);
                if (creditAccount is null)
                    return new ApiResponses().BadRequestResult("That store credit account no longer exists. Look it up again.");
                var toApply = Math.Min(Math.Min(request.CreditCents, creditAccount.BalanceCents), totalCents);
                var wouldBeDue = totalCents - toApply;
                if (paymentMethod != "cash" && toApply > 0 && wouldBeDue > 0 && wouldBeDue < 50)
                    return new ApiResponses().BadRequestResult(
                        "Less than 50 cents would be left for the card after credit. Take the remainder as cash.");
                if (toApply > 0)
                {
                    creditTenderId = await _credit.TryCreateCheckoutTender(
                        _tenantContext.TenantId, creditAccount.Id, toApply, "counter");
                    if (creditTenderId is null)
                        return new ApiResponses().BadRequestResult(
                            "The store credit balance changed while ringing up. Look the customer up again.");
                    creditApplied = toApply;
                }
            }
            var dueCents = totalCents - creditApplied;

            // Books the tender's balancing entry for paths that settle right now (cash, or credit
            // covering everything); card checkouts book it from the payment webhook instead.
            async Task BookTenderNow()
            {
                if (creditTenderId is null) return;
                await _finalizer.BookCreditTenderEntry(new Services.Repositories.Data.CreditData.CheckoutCreditTender
                {
                    Id = creditTenderId.Value,
                    TenantId = _tenantContext.TenantId,
                    CreditAppliedCents = creditApplied,
                }, reduceNet: false);
            }

            // Cash sale: tenant collected the rider's payment directly. We mark every row paid,
            // write a ledger entry per line with negative net_to_tenant equal to the service
            // charge (tenant owes the platform that amount), and short-circuit Stripe. A sale
            // fully covered by store credit settles the same way (nothing left to charge).
            if (paymentMethod == "cash" || (creditApplied > 0 && dueCents == 0))
            {
                var occurredAt = DateTime.UtcNow;
                foreach (var (kind, purchaseId, gross, serviceCharge) in ledgerLines)
                {
                    if (kind == "event_ticket") await _ticketPurchases.UpdateStatus(purchaseId, "paid");
                    else if (kind == "membership") await _memberships.UpdateStatus(purchaseId, "paid");
                    else if (kind == "extras") await _extras.UpdateStatus(purchaseId, "paid");
                    else if (kind == "shop_rental")
                    {
                        if (await _shop.TryMarkRentalPaid(purchaseId, _tenantContext.TenantId))
                            await _shop.SetRentalOrderNumber(purchaseId, await _shop.NextOrderNumber(_tenantContext.TenantId));
                    }
                    try
                    {
                        await _ledger.Insert(new TenantLedgerEntry
                        {
                            TenantId = _tenantContext.TenantId,
                            EntryKind = "sale",
                            SourceKind = kind,
                            SourceId = purchaseId,
                            OccurredAtUtc = occurredAt,
                            GrossCents = gross,
                            StripeFeeCents = 0,
                            RidepassCutCents = serviceCharge,
                            NetToTenantCents = -serviceCharge,
                            PaymentMethod = "cash",
                            SoldByUserId = cashierId,
                            Memo = "Cash sale, tenant owes service charge",
                        });
                    }
                    catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505") { /* idempotent */ }
                }
                if (request.RewardRedemptionId.HasValue)
                {
                    var first = lineItems[0];
                    await _rewards.MarkRedemptionUsed(request.RewardRedemptionId.Value, first.Kind, first.PurchaseId);
                }

                await BookTenderNow();

                // Credit-back loyalty on the cash actually collected, keyed to the first line so
                // a retried submit can't double-award.
                try
                {
                    await _rewardEngine.AwardCreditBack(_tenantContext.TenantId, rider.Id, rider.Email,
                        $"{rider.FirstName} {rider.LastName}".Trim(), "event_ticket",
                        lineItems[0].PurchaseId, dueCents);
                }
                catch { /* loyalty is best-effort; the sale already settled */ }

                // A cash sale at the counter never touches Stripe, so nothing else would email the
                // rider. They still want the entry in their inbox and on their account.
                await SendCounterConfirmations(lineItems);

                return new ApiResponses().OkResult(new CounterSaleResponse
                {
                    ClientSecret = string.Empty,
                    TotalAmountCents = totalCents,
                    TaxCents = ticketTaxCents,
                    CreditAppliedCents = creditApplied,
                    DueCents = dueCents,
                    LineItems = lineItems,
                });
            }

            // Free-cart fast path (rare — happens only when a 100%-off voucher zeroes out a single-line cart).
            if (totalCents == 0)
            {
                foreach (var li in lineItems)
                {
                    if (li.Kind == "event_ticket") await _ticketPurchases.UpdateStatus(li.PurchaseId, "paid");
                    else if (li.Kind == "extras") await _extras.UpdateStatus(li.PurchaseId, "paid");
                    else if (li.Kind == "membership") await _memberships.UpdateStatus(li.PurchaseId, "paid");
                    else if (li.Kind == "shop_rental")
                    {
                        if (await _shop.TryMarkRentalPaid(li.PurchaseId, _tenantContext.TenantId))
                            await _shop.SetRentalOrderNumber(li.PurchaseId, await _shop.NextOrderNumber(_tenantContext.TenantId));
                    }
                    // 'extras' is a valid ledger source_kind (Script0099), so the free path records a
                    // $0 sale row for every line, just like the cash and Stripe paths.
                    try
                    {
                        await _ledger.Insert(new TenantLedgerEntry
                        {
                            TenantId = _tenantContext.TenantId,
                            EntryKind = "sale",
                            SourceKind = li.Kind,
                            SourceId = li.PurchaseId,
                            OccurredAtUtc = DateTime.UtcNow,
                            GrossCents = 0,
                            StripeFeeCents = 0,
                            RidepassCutCents = 0,
                            NetToTenantCents = 0,
                            PaymentMethod = "voucher",
                            Memo = "Free purchase via reward voucher",
                        });
                    }
                    catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505") { /* idempotent */ }
                }
                if (request.RewardRedemptionId.HasValue)
                {
                    var first = lineItems[0];
                    await _rewards.MarkRedemptionUsed(request.RewardRedemptionId.Value, first.Kind, first.PurchaseId);
                }

                // Free at the counter (a 100%-off voucher) is still an admission: same email, same QR.
                await SendCounterConfirmations(lineItems);

                return new ApiResponses().OkResult(new CounterSaleResponse
                {
                    ClientSecret = string.Empty,
                    TotalAmountCents = 0,
                    LineItems = lineItems,
                });
            }

            var metadata = new Dictionary<string, string>
            {
                ["tenant_id"] = _tenantContext.TenantId.ToString(),
                ["rider_id"] = rider.Id.ToString(),
                ["sale_kind"] = "counter",
                ["item_count"] = lineItems.Count.ToString(),
            };
            if (cashierId.HasValue) metadata["sold_by_user_id"] = cashierId.Value.ToString();

            // Direct-charge tenants run the sale on their own connected account (card-present or
            // online); our service fee (summed across line items) rides as the application fee.
            ChargePlan chargePlan;
            try
            {
                chargePlan = _chargeRouter.Plan(_tenantContext.Tenant, ledgerLines.Sum(l => (long)l.ServiceCharge), dueCents);
            }
            catch (InvalidOperationException ex)
            {
                if (creditTenderId is not null)
                    await _credit.ReverseRedeem(_tenantContext.TenantId, "credit_tender", creditTenderId.Value, "payment could not start");
                return new ApiResponses().BadRequestResult(ex.Message);
            }

            // Tap to Pay: identical cart and identical PI-id-keyed webhook fulfillment, but a
            // card_present PaymentIntent scoped to the tenant's Terminal Location so the mobile
            // SDK can collect it. In direct mode the Location lives on the connected account.
            string? terminalLocationId = null;
            if (cardPresent)
            {
                terminalLocationId = await EnsureTerminalLocation(chargePlan.ConnectedAccountId, ct);
                if (terminalLocationId is null)
                {
                    return new ApiResponses().BadRequestResult(
                        "Cannot provision a Stripe Terminal Location for this tenant — fill in the tenant's address (line, city, country, postal code) under Settings first.");
                }
            }

            PaymentIntentCreated intent;
            try
            {
                intent = cardPresent
                    ? await _payments.CreateCardPresentPaymentIntentAsync(
                        amountCents: dueCents,
                        currency: "usd",
                        locationId: terminalLocationId!,
                        metadata: metadata,
                        receiptEmail: rider.Email,
                        connectedAccountId: chargePlan.ConnectedAccountId,
                        applicationFeeCents: chargePlan.ApplicationFeeCents,
                        ct: ct)
                    : await _payments.CreatePaymentIntentAsync(
                        amountCents: dueCents,
                        currency: "usd",
                        metadata: metadata,
                        receiptEmail: rider.Email,
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
            if (creditTenderId is not null)
                await _credit.SetCheckoutTenderPaymentIntent(creditTenderId.Value, _tenantContext.TenantId, intent.IntentId);

            // Stamp the same PaymentIntent id onto every line item so the webhook can finalize them
            // all, and (for a direct charge) snapshot the connected account on each so refunds route
            // to the right account.
            foreach (var li in lineItems)
            {
                switch (li.Kind)
                {
                    case "event_ticket":
                        await _ticketPurchases.SetStripePaymentIntentId(li.PurchaseId, intent.IntentId);
                        if (chargePlan.IsDirect)
                            await _ticketPurchases.MarkDirectCharge(li.PurchaseId, _tenantContext.TenantId, chargePlan.ConnectedAccountId!);
                        break;
                    case "extras":
                        await _extras.SetPaymentIntentId(li.PurchaseId, intent.IntentId);
                        if (chargePlan.IsDirect)
                            await _extras.MarkDirectCharge(li.PurchaseId, _tenantContext.TenantId, chargePlan.ConnectedAccountId!);
                        break;
                    case "membership":
                        await _memberships.SetStripePaymentIntentId(li.PurchaseId, intent.IntentId);
                        if (chargePlan.IsDirect)
                            await _memberships.MarkDirectCharge(li.PurchaseId, _tenantContext.TenantId, chargePlan.ConnectedAccountId!);
                        break;
                    case "shop_rental":
                        await _shop.SetRentalPaymentIntent(li.PurchaseId, intent.IntentId);
                        if (chargePlan.IsDirect)
                            await _shop.MarkRentalDirectCharge(li.PurchaseId, _tenantContext.TenantId, chargePlan.ConnectedAccountId!);
                        break;
                }
            }

            return new ApiResponses().OkResult(new CounterSaleResponse
            {
                ClientSecret = intent.ClientSecret,
                TotalAmountCents = totalCents,
                TaxCents = ticketTaxCents,
                CreditAppliedCents = creditApplied,
                DueCents = dueCents,
                LineItems = lineItems,
                TerminalLocationId = terminalLocationId,
            });
        }

        private static bool IsValidPngDataUrl(string? dataUrl)
        {
            if (string.IsNullOrWhiteSpace(dataUrl)) return false;
            if (!dataUrl.StartsWith("data:image/png;base64,", StringComparison.Ordinal)) return false;
            return dataUrl.Length is > 800 and < 1_400_000;
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

        // ── Stripe Terminal (tap-to-pay) for the RidePassCashier mobile app ─────
        // The mobile SDK needs (1) a connection token to authenticate and (2) a
        // Location id to scope reader discovery. We lazily provision the Location
        // the first time a cashier opens the app at this tenant, using the
        // tenant's address fields as the Stripe Location address.

        [HttpPost("Terminal/ConnectionToken")]
        public async Task<IActionResult> CreateTerminalConnectionToken(CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            // Direct mode: the token + Location must live on the tenant's connected account so the
            // SDK collects card-present payments there.
            if (_tenantContext.Tenant.StripeChargeMode == "direct" && string.IsNullOrEmpty(_tenantContext.Tenant.StripeConnectAccountId))
            {
                return new ApiResponses().BadRequestResult(
                    "This track is set to charge on its own Stripe account but hasn't connected one yet. Connect it in Settings before taking card-present payments.");
            }
            var connectedAccountId = DirectConnectedAccountId();
            var locationId = await EnsureTerminalLocation(connectedAccountId, ct);
            if (locationId is null)
            {
                return new ApiResponses().BadRequestResult(
                    "Cannot provision a Stripe Terminal Location for this tenant — fill in the tenant's address (line, city, country, postal code) under Settings first.");
            }
            string secret;
            try
            {
                secret = await _payments.CreateTerminalConnectionTokenAsync(locationId, connectedAccountId, ct: ct);
            }
            catch (InvalidOperationException ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
            return new ApiResponses().OkResult(new TerminalConnectionTokenResponse
            {
                Secret = secret,
                LocationId = locationId,
            });
        }

        // Minimal card-present PI for the validation milestone — takes an amount
        // and an optional receipt email, creates a PI the mobile SDK can collect
        // and confirm. The full cart-validating endpoint (mirroring CreateSale's
        // gates) lands in v1.5 alongside the mobile cashier UX.
        //
        // This charges an arbitrary amount and writes NO purchase or ledger row, so any money it
        // collects is invisible to sales/refunds/reconciliation. It must never be reachable in
        // production: gate it behind Features:CardPresentTestCharge (default off). Remove the gate
        // when the v1.5 cart-validating endpoint that records a sale replaces it.
        [HttpPost("Terminal/PaymentIntent")]
        public async Task<IActionResult> CreateCardPresentPaymentIntent(
            [FromBody] CardPresentTestChargeRequest req, CancellationToken ct)
        {
            if (!_config.GetValue<bool>("Features:CardPresentTestCharge"))
                return new ApiResponses().BadRequestResult("Card-present test charges are not enabled.");
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (req.AmountCents < 50) return new ApiResponses().BadRequestResult("Amount must be at least 50 cents.");

            if (_tenantContext.Tenant.StripeChargeMode == "direct" && string.IsNullOrEmpty(_tenantContext.Tenant.StripeConnectAccountId))
            {
                return new ApiResponses().BadRequestResult(
                    "This track is set to charge on its own Stripe account but hasn't connected one yet.");
            }
            var connectedAccountId = DirectConnectedAccountId();
            var locationId = await EnsureTerminalLocation(connectedAccountId, ct);
            if (locationId is null)
            {
                return new ApiResponses().BadRequestResult(
                    "Cannot provision a Stripe Terminal Location for this tenant — fill in the tenant's address first.");
            }

            var metadata = new Dictionary<string, string>
            {
                ["tenant_id"] = _tenantContext.TenantId.ToString(),
                ["sale_kind"] = "card_present_test",
            };
            if (Guid.TryParse(User.FindFirst("UserId")?.Value, out var staffId))
            {
                metadata["sold_by_user_id"] = staffId.ToString();
            }

            PaymentIntentCreated intent;
            try
            {
                // A standalone validation charge has no cart/service charge, so no application fee.
                intent = await _payments.CreateCardPresentPaymentIntentAsync(
                    amountCents: req.AmountCents,
                    currency: "usd",
                    locationId: locationId,
                    metadata: metadata,
                    receiptEmail: string.IsNullOrWhiteSpace(req.ReceiptEmail) ? null : req.ReceiptEmail.Trim(),
                    connectedAccountId: connectedAccountId,
                    ct: ct);
            }
            catch (InvalidOperationException ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
            return new ApiResponses().OkResult(new
            {
                paymentIntentId = intent.IntentId,
                clientSecret = intent.ClientSecret,
                amountCents = req.AmountCents,
            });
        }

        // Returns the existing Terminal Location id, or provisions one from the
        // tenant's address fields and persists it. Idempotent — repeated calls
        // for an already-provisioned tenant just return the stored id.
        // The connected account a 'direct' tenant's card-present sales run on (null = platform mode).
        // Card-present direct charges require the Terminal Location, connection token, and PI to all
        // live on this account.
        private string? DirectConnectedAccountId()
            => _tenantContext.Tenant.StripeChargeMode == "direct"
                ? _tenantContext.Tenant.StripeConnectAccountId
                : null;

        // Returns the Terminal Location id for the given account (the tenant's connected account in
        // 'direct' mode, else the platform account), provisioning + persisting it from the tenant's
        // address on first use. Returns null when the address is incomplete. Idempotent.
        private async Task<string?> EnsureTerminalLocation(string? connectedAccountId, CancellationToken ct)
        {
            var tenant = _tenantContext.Tenant;
            var direct = !string.IsNullOrEmpty(connectedAccountId);
            var existing = direct ? tenant.StripeConnectedTerminalLocationId : tenant.StripeTerminalLocationId;
            if (!string.IsNullOrWhiteSpace(existing))
            {
                return existing;
            }
            // Need enough address to satisfy Stripe's Location requirements.
            if (string.IsNullOrWhiteSpace(tenant.AddressLine)
                || string.IsNullOrWhiteSpace(tenant.City)
                || string.IsNullOrWhiteSpace(tenant.Country)
                || string.IsNullOrWhiteSpace(tenant.PostalCode))
            {
                return null;
            }
            string locationId;
            try
            {
                locationId = await _payments.CreateTerminalLocationAsync(
                    tenant.DisplayName,
                    new TerminalLocationAddress(
                        Line1: tenant.AddressLine,
                        City: tenant.City,
                        Country: tenant.Country,
                        PostalCode: tenant.PostalCode,
                        State: tenant.Region),
                    connectedAccountId,
                    ct);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
            if (direct) await _tenants.SetStripeConnectedTerminalLocationId(_tenantContext.TenantId, locationId);
            else await _tenants.SetStripeTerminalLocationId(_tenantContext.TenantId, locationId);
            return locationId;
        }

        // Confirmation for the counter paths that settle without Stripe (cash, and a voucher that
        // zeroes the cart). Card sales are confirmed off the webhook instead, so they don't come
        // through here and can't double-send. Event tickets carry the order; a cart of only add-ons
        // still confirms, so a walk-up spectator gate fee reaches the buyer's inbox.
        private async Task SendCounterConfirmations(List<CounterSaleLineItem> lineItems)
        {
            var ticketIds = lineItems.Where(l => l.Kind == "event_ticket").Select(l => l.PurchaseId).ToList();
            if (ticketIds.Count > 0)
            {
                await _orderConfirmations.SendForTickets(_tenantContext.TenantId, ticketIds);
                return;
            }
            var extraIds = lineItems.Where(l => l.Kind == "extras").Select(l => l.PurchaseId).ToList();
            if (extraIds.Count > 0)
            {
                await _orderConfirmations.SendForExtras(_tenantContext.TenantId, extraIds);
            }
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

    public class CardPresentTestChargeRequest
    {
        public int AmountCents { get; set; } = 100;
        public string? ReceiptEmail { get; set; }

    }
}
