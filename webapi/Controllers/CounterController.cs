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
        private readonly IPasswordHasher<User> _passwordHasher;
        private readonly IRewardRepository _rewards;
        private readonly ITenantLedgerRepository _ledger;
        private readonly IEventExtraRepository _extras;
        private readonly IMembershipRepository _memberships;
        private readonly IFeeCalculator _feeCalculator;
        private readonly ITenantRepository _tenants;
        private readonly IDbHelper _db;
        private readonly ITenantContext _tenantContext;

        public CounterController(
            IUserRepository users,
            IWaiverRepository waivers,
            IEventRepository events,
            IEventTicketTierRepository tiers,
            IEventTicketPurchaseRepository ticketPurchases,
            IPaymentProvider payments,
            IPasswordHasher<User> passwordHasher,
            IRewardRepository rewards,
            ITenantLedgerRepository ledger,
            IEventExtraRepository extras,
            IMembershipRepository memberships,
            IFeeCalculator feeCalculator,
            ITenantRepository tenants,
            IDbHelper db,
            ITenantContext tenantContext)
        {
            _users = users;
            _waivers = waivers;
            _events = events;
            _tiers = tiers;
            _ticketPurchases = ticketPurchases;
            _payments = payments;
            _passwordHasher = passwordHasher;
            _rewards = rewards;
            _ledger = ledger;
            _extras = extras;
            _memberships = memberships;
            _feeCalculator = feeCalculator;
            _tenants = tenants;
            _db = db;
            _tenantContext = tenantContext;
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

            var paymentMethod = string.IsNullOrWhiteSpace(request.PaymentMethod) ? "stripe" : request.PaymentMethod;
            if (paymentMethod is not ("stripe" or "cash"))
            {
                return new ApiResponses().BadRequestResult("paymentMethod must be 'stripe' or 'cash'.");
            }

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
            var ticketItems = new List<(CounterCartItem Item, EventTicketTier Tier, int UnitAmountCents, int UnitServiceChargeCents)>();
            var extrasItems = new List<(CounterCartItem Item, EventExtraProduct Product, EventExtraVariant? Variant,
                                        int UnitAmountCents, int UnitServiceChargeCents, int UnitPriceFrozen)>();
            // Memberships: at most one per cart (every rider has exactly one active membership at a time).
            (CounterCartItem Item, int PriceCents, int ServiceChargeCents)? membershipItem = null;
            int totalCents = 0;
            bool waiverRequiredByCart = false;
            var tenant = _tenantContext.Tenant;
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
                    if (tier.Inventory.HasValue)
                    {
                        var sold = await _tiers.SoldCount(tier.Id);
                        if (sold + item.Quantity > tier.Inventory.Value)
                        {
                            return new ApiResponses().BadRequestResult($"Tier '{tier.Name}' has only {tier.Inventory.Value - sold} left.");
                        }
                    }
                    // Race classes are one-per-rider — block duplicates within the cart
                    // and any earlier active entry for the same rider in this tier.
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
                        var already = await _ticketPurchases.HasActiveRaceEntry(
                            _tenantContext.TenantId, tier.Id, rider.Id, rider.Email);
                        if (already)
                        {
                            return new ApiResponses().BadRequestResult(
                                $"{rider.FirstName} is already entered in '{tier.Name}'.");
                        }
                    }
                    var (unitAmount, unitServiceCharge) = ComputeWithServiceCharge(
                        tier.PriceCents, quantity: 1, tenant.ServiceChargeBps, tier.RiderPaidServiceChargeBps);
                    ticketItems.Add((item, tier, unitAmount, unitServiceCharge));
                    totalCents += unitAmount * item.Quantity;
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
                    var (newUnitAmt, newUnitSc) = ComputeWithServiceCharge(
                        discountedPrice, 1, tenant.ServiceChargeBps, entry.Tier.RiderPaidServiceChargeBps);
                    totalCents -= entry.UnitAmountCents;
                    totalCents += newUnitAmt;
                    // Stash the discounted unit by pre-pending a synthetic entry of qty=1 and trimming the original.
                    var trimmedItem = new CounterCartItem { Kind = entry.Item.Kind, ItemId = entry.Item.ItemId, Quantity = entry.Item.Quantity - 1 };
                    var discountedItem = new CounterCartItem { Kind = entry.Item.Kind, ItemId = entry.Item.ItemId, Quantity = 1 };
                    ticketItems.RemoveAt(tki);
                    ticketItems.Insert(0, (discountedItem, entry.Tier, newUnitAmt, newUnitSc));
                    if (trimmedItem.Quantity > 0)
                    {
                        ticketItems.Insert(1, (trimmedItem, entry.Tier, entry.UnitAmountCents, entry.UnitServiceChargeCents));
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
                foreach (var (rcItem, rcTier, _, _) in ticketItems)
                {
                    if (rcTier.Inventory.HasValue)
                    {
                        var soldNow = await _tiers.SoldCount(rcTier.Id);
                        if (soldNow + rcItem.Quantity > rcTier.Inventory.Value)
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
                var (item, tier, unitAmount, unitServiceCharge) = ticketItems[tkiIdx];
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
                        AppliedRewardRedemptionId = applyVoucherHere ? request.RewardRedemptionId : null,
                        PaymentMethod = paymentMethod,
                        Status = "pending",
                        PurchaserEmail = rider.Email,
                        PurchaserName = purchaserName,
                        SoldByUserId = cashierId,
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
            // Extras: one event_extra_purchase row per unit so each gets its own QR.
            // Variant attrs frozen on the row. Source kind 'extras' isn't in the
            // tenant_ledger CHECK constraint yet, so we skip ledger inserts for now —
            // matching how the existing extras flow handles ledger writes.
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
                }
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

            // Cash sale: tenant collected the rider's payment directly. We mark every row paid,
            // write a ledger entry per line with negative net_to_tenant equal to the service
            // charge (tenant owes the platform that amount), and short-circuit Stripe.
            if (paymentMethod == "cash")
            {
                var occurredAt = DateTime.UtcNow;
                foreach (var (kind, purchaseId, gross, serviceCharge) in ledgerLines)
                {
                    if (kind == "event_ticket") await _ticketPurchases.UpdateStatus(purchaseId, "paid");
                    else if (kind == "membership") await _memberships.UpdateStatus(purchaseId, "paid");
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
                            Memo = "Cash sale — tenant owes service charge",
                        });
                    }
                    catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505") { /* idempotent */ }
                }
                // Extras don't go through ledgerLines (no source_kind='extras' in the
                // tenant_ledger CHECK constraint), so flip their status here.
                foreach (var li in lineItems.Where(l => l.Kind == "extras"))
                {
                    await _extras.UpdateStatus(li.PurchaseId, "paid");
                }
                if (request.RewardRedemptionId.HasValue)
                {
                    var first = lineItems[0];
                    await _rewards.MarkRedemptionUsed(request.RewardRedemptionId.Value, first.Kind, first.PurchaseId);
                }
                return new ApiResponses().OkResult(new CounterSaleResponse
                {
                    ClientSecret = string.Empty,
                    TotalAmountCents = totalCents,
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
                    // Extras still has no source_kind in the ledger CHECK, so skip them here too.
                    if (li.Kind == "extras") continue;
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

            PaymentIntentCreated intent;
            try
            {
                intent = await _payments.CreatePaymentIntentAsync(
                    amountCents: totalCents,
                    currency: "usd",
                    metadata: metadata,
                    receiptEmail: rider.Email,
                    ct: ct);
            }
            catch (InvalidOperationException ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }

            // Stamp the same PaymentIntent id onto every line item so the webhook can finalize them all.
            foreach (var li in lineItems)
            {
                switch (li.Kind)
                {
                    case "event_ticket":
                        await _ticketPurchases.SetStripePaymentIntentId(li.PurchaseId, intent.IntentId);
                        break;
                    case "extras":
                        await _extras.SetPaymentIntentId(li.PurchaseId, intent.IntentId);
                        break;
                    case "membership":
                        await _memberships.SetStripePaymentIntentId(li.PurchaseId, intent.IntentId);
                        break;
                }
            }

            return new ApiResponses().OkResult(new CounterSaleResponse
            {
                ClientSecret = intent.ClientSecret,
                TotalAmountCents = totalCents,
                LineItems = lineItems,
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
            var locationId = await EnsureTerminalLocation(ct);
            if (locationId is null)
            {
                return new ApiResponses().BadRequestResult(
                    "Cannot provision a Stripe Terminal Location for this tenant — fill in the tenant's address (line, city, country, postal code) under Settings first.");
            }
            string secret;
            try
            {
                secret = await _payments.CreateTerminalConnectionTokenAsync(locationId, ct);
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
        [HttpPost("Terminal/PaymentIntent")]
        public async Task<IActionResult> CreateCardPresentPaymentIntent(
            [FromBody] CardPresentTestChargeRequest req, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (req.AmountCents < 50) return new ApiResponses().BadRequestResult("Amount must be at least 50 cents.");

            var locationId = await EnsureTerminalLocation(ct);
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
                intent = await _payments.CreateCardPresentPaymentIntentAsync(
                    amountCents: req.AmountCents,
                    currency: "usd",
                    locationId: locationId,
                    metadata: metadata,
                    receiptEmail: string.IsNullOrWhiteSpace(req.ReceiptEmail) ? null : req.ReceiptEmail.Trim(),
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
        private async Task<string?> EnsureTerminalLocation(CancellationToken ct)
        {
            var tenant = _tenantContext.Tenant;
            if (!string.IsNullOrWhiteSpace(tenant.StripeTerminalLocationId))
            {
                return tenant.StripeTerminalLocationId;
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
                    ct);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
            await _tenants.SetStripeTerminalLocationId(_tenantContext.TenantId, locationId);
            return locationId;
        }
    }

    public class CardPresentTestChargeRequest
    {
        public int AmountCents { get; set; } = 100;
        public string? ReceiptEmail { get; set; }
    }
}
