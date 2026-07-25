using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Payments;
using Services.Repositories.Data.BikeShopData;
using Services.Repositories.Data.PackageData;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Data.TenantData;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Package;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    // Packages: a bundled product (modeled on "Find Your Ride") = a coached session +
    // day admission + a bike + gear, sold at day-type tiers, with a landing page. A
    // booking composes a real gate ticket and a real shop rental, split-priced from the
    // tier, settled under one payment via the "package" webhook branch.
    [ApiController]
    [Route("api/[controller]")]
    public class PackageController : ControllerBase
    {
        private readonly IPackageRepository _packages;
        private readonly IEventRepository _events;
        private readonly IEventTicketTierRepository _tiers;
        private readonly IEventTicketPurchaseRepository _tickets;
        private readonly IBikeShopRepository _shop;
        private readonly IUserRepository _users;
        private readonly IChargeRouter _chargeRouter;
        private readonly IPaymentProvider _payments;
        private readonly ITenantContext _tenantContext;

        public PackageController(IPackageRepository packages, IEventRepository events,
            IEventTicketTierRepository tiers, IEventTicketPurchaseRepository tickets,
            IBikeShopRepository shop, IUserRepository users, IChargeRouter chargeRouter,
            IPaymentProvider payments, ITenantContext tenantContext)
        {
            _packages = packages;
            _events = events;
            _tiers = tiers;
            _tickets = tickets;
            _shop = shop;
            _users = users;
            _chargeRouter = chargeRouter;
            _payments = payments;
            _tenantContext = tenantContext;
        }

        private Guid TenantId => _tenantContext.TenantId;

        // ── Admin: CRUD ──────────────────────────────────────────────────────────
        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpGet("Admin")]
        public async Task<IActionResult> ListAdmin()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var rows = await _packages.ListByTenant(TenantId);
            var full = new List<PackageResponse>();
            foreach (var r in rows)
            {
                var p = await _packages.GetById(r.Id, TenantId);
                if (p is not null) full.Add(ToResponse(p));
            }
            return new ApiResponses().OkResult(full);
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpGet("Admin/{id:guid}")]
        public async Task<IActionResult> GetAdmin(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var p = await _packages.GetById(id, TenantId);
            return p is null ? new ApiResponses().NotFoundResult("Package not found.") : new ApiResponses().OkResult(ToResponse(p));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] UpsertPackageRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (string.IsNullOrWhiteSpace(req.Name)) return new ApiResponses().BadRequestResult("A name is required.");
            var id = await _packages.Create(FromRequest(new PackageProduct { TenantId = TenantId }, req));
            await SaveChildren(id, req);
            var saved = await _packages.GetById(id, TenantId);
            return new ApiResponses().OkResult(ToResponse(saved!));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpsertPackageRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var existing = await _packages.GetById(id, TenantId);
            if (existing is null) return new ApiResponses().NotFoundResult("Package not found.");
            existing.Id = id;
            await _packages.Update(FromRequest(existing, req));
            await SaveChildren(id, req);
            var saved = await _packages.GetById(id, TenantId);
            return new ApiResponses().OkResult(ToResponse(saved!));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            await _packages.Delete(id, TenantId);
            return new ApiResponses().OkResult();
        }

        // ── Public: landing + list ───────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> ListPublic()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var rows = await _packages.ListPublic(TenantId);
            return new ApiResponses().OkResult(rows.Select(ToResponse));
        }

        [HttpGet("Landing/{slugOrId}")]
        public async Task<IActionResult> GetLanding(string slugOrId)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var p = await _packages.GetBySlugOrId(slugOrId, TenantId);
            if (p is null || !p.IsActive) return new ApiResponses().NotFoundResult("This package isn't available.");
            return new ApiResponses().OkResult(ToResponse(p));
        }

        // ── Public: availability + price for a chosen date + tier ────────────────
        [HttpGet("Availability")]
        public async Task<IActionResult> Availability([FromQuery] string package, [FromQuery] DateTime date,
            [FromQuery] Guid tierId)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var p = await _packages.GetBySlugOrId(package, TenantId);
            if (p is null || !p.IsActive) return new ApiResponses().NotFoundResult("Package not found.");
            var tier = p.Tiers.FirstOrDefault(t => t.Id == tierId && t.IsActive);
            if (tier is null) return new ApiResponses().BadRequestResult("Pick a valid option.");

            var rideDate = DateOnly.FromDateTime(date);
            var resp = new PackageAvailabilityResponse { PriceCents = tier.PriceCents };

            if (!DayMatches(tier.DayScope, rideDate))
            {
                resp.Available = false;
                resp.Reason = $"The {tier.Name} option isn't available on that day.";
                return new ApiResponses().OkResult(resp);
            }

            var (variants, deposit, rentalGross, availErr) = await ResolveItems(p, rideDate);
            resp.DepositCents = deposit;
            resp.InsuranceCents = InsuranceFor(rentalGross);
            if (availErr is not null) { resp.Available = false; resp.Reason = availErr; return new ApiResponses().OkResult(resp); }

            if (p.IncludesDayTicket && await FindGateTier(p, rideDate) is null)
            {
                resp.Available = false;
                resp.Reason = "No admission is scheduled for that date. Pick another day.";
                return new ApiResponses().OkResult(resp);
            }

            // Coached session times for the date, with remaining capacity.
            if (p.CoachingMinutes is not null)
            {
                foreach (var slot in p.Slots.Where(s => s.IsActive && DayMatches(s.DayScope, rideDate)
                    && (!tier.AfternoonOnly || s.IsAfternoon)).OrderBy(s => s.StartTime))
                {
                    var booked = await _packages.CountSlotBookings(slot.Id, date.Date);
                    var remaining = Math.Max(0, slot.Capacity - booked);
                    if (remaining > 0)
                        resp.Sessions.Add(new PackageSlotAvailability
                        {
                            SlotId = slot.Id,
                            StartTime = slot.StartTime.ToString(@"hh\:mm"),
                            Remaining = remaining,
                        });
                }
                if (resp.Sessions.Count == 0)
                {
                    resp.Available = false;
                    resp.Reason = "All coached sessions for that date are full. Try another day.";
                    return new ApiResponses().OkResult(resp);
                }
            }

            resp.Available = true;
            return new ApiResponses().OkResult(resp);
        }

        // ── Public: book (signed-in rider) ───────────────────────────────────────
        [Authorize]
        [HttpPost("Book")]
        public async Task<IActionResult> Book([FromBody] BookPackageRequest req, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var tenant = _tenantContext.Tenant;
            if (!Guid.TryParse(User.FindFirst("UserId")?.Value, out var userId))
                return new ApiResponses().BadRequestResult("Not signed in.");
            var user = await _users.GetById(userId);
            if (user is null) return new ApiResponses().BadRequestResult("User not found.");

            var p = await _packages.GetById(req.PackageId, TenantId);
            if (p is null || !p.IsActive) return new ApiResponses().BadRequestResult("This package isn't available.");
            var tier = p.Tiers.FirstOrDefault(t => t.Id == req.TierId && t.IsActive);
            if (tier is null) return new ApiResponses().BadRequestResult("Pick a valid option.");

            var rideDate = DateOnly.FromDateTime(req.RideDate);
            if (rideDate < DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)))
                return new ApiResponses().BadRequestResult("That ride date is in the past.");
            if (!DayMatches(tier.DayScope, rideDate))
                return new ApiResponses().BadRequestResult($"The {tier.Name} option isn't available on that day.");

            // Coached session slot.
            PackageSessionSlot? slot = null;
            DateTime? sessionStartUtc = null;
            if (p.CoachingMinutes is not null)
            {
                if (req.SlotId is null) return new ApiResponses().BadRequestResult("Choose a coached session time.");
                slot = p.Slots.FirstOrDefault(s => s.Id == req.SlotId && s.IsActive);
                if (slot is null || !DayMatches(slot.DayScope, rideDate) || (tier.AfternoonOnly && !slot.IsAfternoon))
                    return new ApiResponses().BadRequestResult("That session time isn't available for this option.");
                var booked = await _packages.CountSlotBookings(slot.Id, req.RideDate.Date);
                if (booked >= slot.Capacity)
                    return new ApiResponses().BadRequestResult("That session just filled up. Pick another time.");
                sessionStartUtc = ToUtc(rideDate, slot.StartTime, tenant.Timezone);
            }

            // Day admission: the gate tier of the day's event.
            EventTicketTier? gateTier = null;
            if (p.IncludesDayTicket)
            {
                gateTier = await FindGateTier(p, rideDate);
                if (gateTier is null) return new ApiResponses().BadRequestResult("No admission is scheduled for that date.");
            }

            // Optional bike-size choice: swap the (first) bike item to the chosen sibling variant.
            if (req.BikeVariantId is Guid chosenBikeVariant)
            {
                var bikeItem = p.Items.FirstOrDefault(i => i.ItemType == "bike");
                if (bikeItem is not null && bikeItem.VariantId != chosenBikeVariant)
                {
                    var chosen = await _shop.GetVariant(chosenBikeVariant, TenantId);
                    var current = await _shop.GetVariant(bikeItem.VariantId, TenantId);
                    if (chosen is null || current is null || chosen.ProductId != current.ProductId || chosen.DailyRateCents is null)
                        return new ApiResponses().BadRequestResult("That bike size isn't available.");
                    bikeItem.VariantId = chosenBikeVariant;
                }
            }

            // Included gear, priced and reserved for the day window.
            var dayStart = ToUtc(rideDate, TimeSpan.Zero, tenant.Timezone);
            var dayEnd = dayStart.AddDays(1);
            var lines = new List<ShopRentalLine>();
            int rentalAlaCarte = 0, deposit = 0, riders = 0;
            foreach (var item in p.Items.OrderBy(i => i.SortOrder))
            {
                var variant = await _shop.GetVariant(item.VariantId, TenantId);
                if (variant is null || variant.DailyRateCents is null)
                    return new ApiResponses().BadRequestResult("An included rental is no longer available.");
                var label = string.Join(" / ", new[] { variant.Size, variant.Color }.Where(s => !string.IsNullOrWhiteSpace(s)));
                var name = item.VariantName ?? "Rental";
                if (variant.TrackingKind == "serialized")
                {
                    var free = await _shop.GetFreeSerializedUnits(item.VariantId, TenantId, dayStart, dayEnd);
                    if (free.Count < item.Quantity)
                        return new ApiResponses().BadRequestResult($"\"{name}\" isn't available on that date.");
                    foreach (var unit in free.Take(item.Quantity))
                    {
                        rentalAlaCarte += variant.DailyRateCents.Value;
                        deposit += variant.DepositCents;
                        if (item.ItemType == "bike") riders += 1;
                        lines.Add(new ShopRentalLine
                        {
                            VariantId = item.VariantId, ItemId = unit.Id, Quantity = 1,
                            NameSnapshot = name, VariantLabel = string.IsNullOrWhiteSpace(label) ? null : label,
                            DailyRateCentsFrozen = variant.DailyRateCents.Value, DepositCentsFrozen = variant.DepositCents,
                            LineAmountCents = variant.DailyRateCents.Value,
                        });
                    }
                }
                else
                {
                    var avail = await _shop.GetPoolAvailability(item.VariantId, TenantId, dayStart, dayEnd);
                    if (item.Quantity > avail)
                        return new ApiResponses().BadRequestResult($"\"{name}\" isn't available on that date.");
                    rentalAlaCarte += variant.DailyRateCents.Value * item.Quantity;
                    deposit += variant.DepositCents * item.Quantity;
                    if (item.ItemType == "bike") riders += item.Quantity;
                    lines.Add(new ShopRentalLine
                    {
                        VariantId = item.VariantId, ItemId = null, Quantity = item.Quantity,
                        NameSnapshot = name, VariantLabel = string.IsNullOrWhiteSpace(label) ? null : label,
                        DailyRateCentsFrozen = variant.DailyRateCents.Value, DepositCentsFrozen = variant.DepositCents,
                        LineAmountCents = variant.DailyRateCents.Value * item.Quantity,
                    });
                }
            }

            // Optional damage waiver: a non-refundable add-on = rate * gross rental value, booked as
            // rental revenue, that waives the refundable deposit hold.
            var insuranceCents = req.Insurance ? InsuranceFor(rentalAlaCarte) : 0;
            var effectiveDeposit = insuranceCents > 0 ? 0 : deposit;

            // Split the bundle price across the admission and the rental by their a-la-carte
            // value, so each row books its real share of revenue.
            var bundle = tier.PriceCents;
            var ticketAlaCarte = gateTier?.PriceCents ?? 0;
            var alaCarte = ticketAlaCarte + rentalAlaCarte;
            var ticketShare = alaCarte > 0 ? (int)((long)bundle * ticketAlaCarte / alaCarte) : (gateTier is not null ? bundle : 0);
            var rentalShare = bundle - ticketShare + insuranceCents;   // insurance rides with the rental

            // Fee + tax on the bundle plus insurance, using the rental fee/tax config (packages are rental-heavy).
            var chargeSubtotal = bundle + insuranceCents;
            var serviceChargeCents = (int)((long)chargeSubtotal * tenant.ServiceChargeBps / 10_000L);
            var renterFeeCents = (int)((long)serviceChargeCents * tenant.RentalRiderPaidServiceChargeBps / 10_000L);
            var taxableBase = chargeSubtotal + (tenant.RentalTaxServiceChargeTaxable ? renterFeeCents : 0);
            var taxCents = (int)Math.Round((decimal)taxableBase * (tenant.RentalTaxBps ?? 0) / 10_000m, MidpointRounding.AwayFromZero);
            var total = chargeSubtotal + renterFeeCents + taxCents;
            if (total < 50) return new ApiResponses().BadRequestResult("A package must total at least 50 cents.");
            if (tenant.StripeChargeMode == "direct" && string.IsNullOrEmpty(tenant.StripeConnectAccountId))
                return new ApiResponses().BadRequestResult("This track can't take online payments yet.");

            // ── Compose: package_purchase + gate ticket + rental ─────────────────
            var purchase = new PackagePurchase
            {
                TenantId = TenantId, PackageId = p.Id, TierId = tier.Id, BuyerUserId = userId,
                BuyerName = $"{user.FirstName} {user.LastName}".Trim(), BuyerEmail = user.Email,
                RideDate = req.RideDate.Date, SessionStartAt = sessionStartUtc, SlotId = slot?.Id,
                InstructorId = slot?.InstructorId, Status = "pending",
                SubtotalCents = chargeSubtotal, TaxCents = taxCents, TotalCents = total,
                DepositCents = effectiveDeposit, ServiceChargeCents = serviceChargeCents,
            };
            var purchaseId = await _packages.CreatePurchase(purchase);

            Guid? ticketId = null;
            if (gateTier is not null)
            {
                var (tid, _) = await _tickets.Create(new EventTicketPurchase
                {
                    TenantId = TenantId, TierId = gateTier.Id, PurchaserUserId = userId,
                    PurchaserName = purchase.BuyerName ?? user.Email, PurchaserEmail = user.Email,
                    AmountCents = ticketShare, Status = "pending", PaymentMethod = "stripe",
                    RiderFirstName = user.FirstName, RiderLastName = user.LastName,
                });
                ticketId = tid;
            }

            Guid? rentalId = null;
            if (lines.Count > 0)
            {
                var rental = new ShopRental
                {
                    TenantId = TenantId, RenterUserId = userId,
                    RenterName = purchase.BuyerName, RenterEmail = user.Email, RenterPhone = user.Phone,
                    StartsAt = dayStart, EndsAt = dayEnd, Status = "pending",
                    AmountCents = rentalShare, TaxCents = 0, TotalCents = rentalShare, ServiceChargeCents = 0,
                    RidersRequired = Math.Max(1, riders), DepositCents = effectiveDeposit,
                    PaymentMethod = "stripe", SoldByUserId = null,
                };
                var (rid, _) = await _shop.CreateRental(rental, lines);
                rentalId = rid;
            }
            await _packages.SetPurchaseArtifacts(purchaseId, ticketId, rentalId);

            // ── One payment for the bundle + one deposit hold ────────────────────
            var metadata = new Dictionary<string, string>
            {
                ["tenant_id"] = TenantId.ToString(),
                ["sale_kind"] = "package",
                ["package_purchase_id"] = purchaseId.ToString(),
            };
            string? depositClientSecret = null;
            PaymentIntentCreated intent;
            ChargePlan plan;
            try
            {
                plan = _chargeRouter.Plan(tenant, serviceFeeCents: serviceChargeCents, chargeAmountCents: total);
                intent = await _payments.CreatePaymentIntentAsync(total, "usd", metadata, user.Email,
                    connectedAccountId: plan.ConnectedAccountId, applicationFeeCents: plan.ApplicationFeeCents, ct: ct);
                await _packages.SetPurchasePaymentIntent(purchaseId, intent.IntentId, null, plan.IsDirect ? plan.ConnectedAccountId : null);
                // The composed rows share this PI (no own fee PI) so they aren't double-settled;
                // the "package" finalizer branch settles them and splits the fee.
                if (rentalId is not null && plan.IsDirect) await _shop.MarkRentalDirectCharge(rentalId.Value, TenantId, plan.ConnectedAccountId!);

                if (effectiveDeposit > 0)
                {
                    var holdMeta = new Dictionary<string, string>(metadata) { ["sale_kind"] = "package_deposit_hold" };
                    var hold = await _payments.CreateHoldPaymentIntentAsync(effectiveDeposit, "usd", holdMeta, user.Email,
                        connectedAccountId: plan.ConnectedAccountId, ct: ct);
                    await _packages.SetPurchasePaymentIntent(purchaseId, intent.IntentId, hold.IntentId, plan.IsDirect ? plan.ConnectedAccountId : null);
                    // Bind the hold to the rental so the existing return-time capture/release works.
                    if (rentalId is not null) await _shop.SetRentalDepositIntent(rentalId.Value, hold.IntentId);
                    depositClientSecret = hold.ClientSecret;
                }
            }
            catch (InvalidOperationException ex)
            {
                await _packages.MarkPurchaseFailed(purchaseId);
                return new ApiResponses().BadRequestResult(ex.Message);
            }

            return new ApiResponses().OkResult(new PackageBookResult
            {
                PurchaseId = purchaseId, Status = "pending",
                ClientSecret = intent.ClientSecret, DepositClientSecret = depositClientSecret,
                TotalCents = total, DepositCents = deposit,
            });
        }

        // ── Helpers ──────────────────────────────────────────────────────────────
        private static bool DayMatches(string scope, DateOnly date)
        {
            var weekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            return scope switch { "weekday" => !weekend, "weekend" => weekend, _ => true };
        }

        private static DateTime ToUtc(DateOnly date, TimeSpan time, string tz)
        {
            TimeZoneInfo zone;
            try { zone = TimeZoneInfo.FindSystemTimeZoneById(tz); } catch { zone = TimeZoneInfo.Utc; }
            var local = date.ToDateTime(TimeOnly.FromTimeSpan(time));
            return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), zone);
        }

        private async Task<EventTicketTier?> FindGateTier(PackageProduct p, DateOnly rideDate)
        {
            var tz = _tenantContext.Tenant.Timezone;
            var dayStart = ToUtc(rideDate, TimeSpan.Zero, tz);
            var dayEnd = dayStart.AddDays(1);
            var events = await _events.GetInRange(TenantId, dayStart, dayEnd);
            foreach (var e in events)
            {
                var tiers = await _tiers.GetForEvent(e.Id, TenantId, activeOnly: true);
                var gate = tiers.FirstOrDefault(t => t.Kind == "gate_fee" && t.Audience == "rider");
                if (gate is not null) return gate;
            }
            return null;
        }

        private async Task<(List<Guid> variantIds, int deposit, int rentalGross, string? error)> ResolveItems(PackageProduct p, DateOnly rideDate)
        {
            var tz = _tenantContext.Tenant.Timezone;
            var dayStart = ToUtc(rideDate, TimeSpan.Zero, tz);
            var dayEnd = dayStart.AddDays(1);
            var ids = new List<Guid>();
            var deposit = 0;
            var rentalGross = 0;
            foreach (var item in p.Items)
            {
                var variant = await _shop.GetVariant(item.VariantId, TenantId);
                if (variant is null || variant.DailyRateCents is null) return (ids, deposit, rentalGross, "An included rental is no longer available.");
                var free = variant.TrackingKind == "serialized"
                    ? (await _shop.GetFreeSerializedUnits(item.VariantId, TenantId, dayStart, dayEnd)).Count
                    : await _shop.GetPoolAvailability(item.VariantId, TenantId, dayStart, dayEnd);
                if (free < item.Quantity) return (ids, deposit, rentalGross, $"\"{item.VariantName ?? "A rental"}\" isn't available on that date.");
                deposit += variant.DepositCents * item.Quantity;
                rentalGross += variant.DailyRateCents.Value * item.Quantity;
                ids.Add(item.VariantId);
            }
            return (ids, deposit, rentalGross, null);
        }

        private int InsuranceFor(int rentalGross)
        {
            // Through RentalCharge so all three booking paths (counter, storefront, packages) price
            // the waiver from one place. Same formula it always was; the tests pin it now.
            //
            // taken: true because this method answers "what would the waiver cost on this rental",
            // which is what the quote at :153 needs. Whether the customer actually took it is the
            // caller's business, and :302 gates on req.Insurance before calling.
            var t = _tenantContext.Tenant;
            return Services.Payments.RentalCharge.InsuranceFor(
                rentalGross, t.RentalInsuranceBps, InsuranceOffered(t), taken: true);
        }

        private async Task SaveChildren(Guid id, UpsertPackageRequest req)
        {
            await _packages.ReplaceTiers(id, TenantId, req.Tiers.Select((t, i) => new PackageTier
            {
                Name = t.Name, PriceCents = t.PriceCents, DayScope = t.DayScope, AfternoonOnly = t.AfternoonOnly,
                SessionCount = Math.Max(1, t.SessionCount), SortOrder = t.SortOrder == 0 ? i : t.SortOrder, IsActive = t.IsActive,
            }));
            await _packages.ReplaceSlots(id, TenantId, req.Slots.Select((s, i) => new PackageSessionSlot
            {
                DayScope = s.DayScope, StartTime = ParseTime(s.StartTime), IsAfternoon = s.IsAfternoon,
                Capacity = Math.Max(1, s.Capacity), InstructorId = s.InstructorId,
                SortOrder = s.SortOrder == 0 ? i : s.SortOrder, IsActive = s.IsActive,
            }));
            await _packages.ReplaceItems(id, TenantId, req.Items.Select((it, i) => new PackageItem
            {
                ItemType = it.ItemType == "bike" ? "bike" : "gear", VariantId = it.VariantId,
                Quantity = Math.Max(1, it.Quantity), SortOrder = it.SortOrder == 0 ? i : it.SortOrder,
            }));
        }

        private static TimeSpan ParseTime(string s) => TimeSpan.TryParse(s, out var t) ? t : new TimeSpan(9, 0, 0);

        private static PackageProduct FromRequest(PackageProduct p, UpsertPackageRequest req)
        {
            p.Name = req.Name.Trim();
            p.Slug = string.IsNullOrWhiteSpace(req.Slug) ? null : req.Slug.Trim().ToLowerInvariant();
            p.Summary = req.Summary;
            p.Description = req.Description;
            p.HeroImageUrl = req.HeroImageUrl;
            p.LandingPublished = req.LandingPublished;
            p.IncludesDayTicket = req.IncludesDayTicket;
            p.DayTicketEventTypeCode = string.IsNullOrWhiteSpace(req.DayTicketEventTypeCode) ? "open_ride" : req.DayTicketEventTypeCode;
            p.CoachingMinutes = req.CoachingMinutes;
            p.CoachingLabel = req.CoachingLabel;
            p.IsActive = req.IsActive;
            p.SortOrder = req.SortOrder;
            p.ValidFromDate = req.ValidFromDate;
            p.ValidToDate = req.ValidToDate;
            return p;
        }

        private PackageResponse ToResponse(PackageProduct p) => new()
        {
            Id = p.Id, Name = p.Name, Slug = p.Slug, Summary = p.Summary, Description = p.Description,
            HeroImageUrl = p.HeroImageUrl, LandingPublished = p.LandingPublished, IncludesDayTicket = p.IncludesDayTicket,
            CoachingMinutes = p.CoachingMinutes, CoachingLabel = p.CoachingLabel, IsActive = p.IsActive, SortOrder = p.SortOrder,
            InsuranceOffered = InsuranceOffered(_tenantContext.Tenant),
            InsuranceLabel = InsuranceLabel(_tenantContext.Tenant),
            Tiers = p.Tiers.Select(t => new PackageTierResponse
            {
                Id = t.Id, Name = t.Name, PriceCents = t.PriceCents, DayScope = t.DayScope,
                AfternoonOnly = t.AfternoonOnly, SessionCount = t.SessionCount, SortOrder = t.SortOrder,
            }).ToList(),
            Slots = p.Slots.Select(s => new PackageSlotResponse
            {
                Id = s.Id, DayScope = s.DayScope, StartTime = s.StartTime.ToString(@"hh\:mm"),
                IsAfternoon = s.IsAfternoon, Capacity = s.Capacity, InstructorId = s.InstructorId,
            }).ToList(),
            Items = p.Items.Select(it => new PackageItemResponse
            {
                Id = it.Id, ItemType = it.ItemType, VariantId = it.VariantId, Quantity = it.Quantity,
                Name = it.VariantName, VariantLabel = it.VariantLabel, DepositCents = it.DepositCents,
                SizeOptions = it.SizeOptions.Select(o => new PackageBikeSizeOptionResponse
                {
                    VariantId = o.VariantId, Label = o.Label, DepositCents = o.DepositCents,
                }).ToList(),
            }).ToList(),
        };

        private static bool InsuranceOffered(Tenant t) => t.RentalInsuranceEnabled && t.RentalInsuranceBps > 0;
        private static string? InsuranceLabel(Tenant t) =>
            InsuranceOffered(t) ? (string.IsNullOrWhiteSpace(t.RentalInsuranceLabel) ? "Damage Protection" : t.RentalInsuranceLabel) : null;
    }
}
