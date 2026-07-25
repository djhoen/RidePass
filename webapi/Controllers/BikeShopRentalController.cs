using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Payments;
using Services.Repositories.Data.BikeShopData;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.BikeShop;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    // Counter-side rentals on the unified shop catalog: check availability for a window, book,
    // hand gear out, take it back (releasing or capturing the deposit hold). Booking reserves
    // capacity by window overlap; stock only moves at checkout/return.
    // Class-level auth is bare [Authorize]: counter actions each carry the ShopCounter policy
    // (attribute policies COMBINE, so a class-level policy could not be relaxed for Mine).
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class BikeShopRentalController : ControllerBase
    {
        private readonly IBikeShopRepository _shop;
        private readonly IChargeRouter _chargeRouter;
        private readonly IPaymentProvider _payments;
        private readonly IFeeCalculator _feeCalculator;
        private readonly ITenantLedgerRepository _ledger;
        private readonly ISeasonPassRepository _seasonPasses;
        private readonly IUserRepository _users;
        private readonly IWaiverRepository _waivers;
        private readonly ISmtpEmailer _emailer;
        private readonly IConfiguration _config;
        private readonly ITenantContext _tenantContext;

        public BikeShopRentalController(IBikeShopRepository shop, IChargeRouter chargeRouter,
            IPaymentProvider payments, IFeeCalculator feeCalculator, ITenantLedgerRepository ledger,
            ISeasonPassRepository seasonPasses, IUserRepository users,
            IWaiverRepository waivers,
            ISmtpEmailer emailer,
            IConfiguration config,
            ITenantContext tenantContext)
        {
            _shop = shop;
            _chargeRouter = chargeRouter;
            _payments = payments;
            _feeCalculator = feeCalculator;
            _ledger = ledger;
            _seasonPasses = seasonPasses;
            _users = users;
            _waivers = waivers;
            _emailer = emailer;
            _config = config;
            _tenantContext = tenantContext;
        }

        private Guid TenantId => _tenantContext.TenantId;
        private Guid? UserId => Guid.TryParse(User.FindFirst("UserId")?.Value, out var id) ? id : null;

        [Authorize(Policy = TenantPermissions.Policy.ShopCounter)]
        [HttpGet("Availability")]
        public async Task<IActionResult> Availability([FromQuery] Guid variantId,
            [FromQuery] DateTime startsAt, [FromQuery] DateTime endsAt)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (endsAt <= startsAt) return new ApiResponses().BadRequestResult("The return time must be after the start time.");
            var variant = await _shop.GetVariant(variantId, TenantId);
            if (variant is null) return new ApiResponses().NotFoundResult("Variant not found.");
            if (variant.TrackingKind == "serialized")
            {
                var units = await _shop.GetFreeSerializedUnits(variantId, TenantId, startsAt, endsAt);
                return new ApiResponses().OkResult(new { available = units.Count, units });
            }
            var available = await _shop.GetPoolAvailability(variantId, TenantId, startsAt, endsAt);
            return new ApiResponses().OkResult(new { available, units = Array.Empty<object>() });
        }

        // The Rental Board timeline: the whole rentable fleet plus every reservation overlapping
        // the window, in one round trip. The Availability action above answers "how many free"
        // with a scalar per variant, which a timeline can't draw from.
        //
        // The payload is deliberately self-contained (rates, deposits, and the category list ride
        // along) because BikeShop/Categories and BikeShop/Products sit behind CatalogManage while
        // this is a ShopCounter screen, and a counter-only user must get a working board and filter.
        [Authorize(Policy = TenantPermissions.Policy.ShopCounter)]
        [HttpGet("Board")]
        public async Task<IActionResult> Board([FromQuery] DateTime startsAt, [FromQuery] DateTime endsAt,
            [FromQuery] Guid? categoryId = null)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var from = startsAt.ToUniversalTime();
            var to = endsAt.ToUniversalTime();
            if (to <= from) return new ApiResponses().BadRequestResult("The end of the window must be after the start.");
            // A board is a day or a week, never a year. Cap it so a stray query string can't ask
            // for every reservation the track has ever taken.
            if ((to - from).TotalDays > 31)
                return new ApiResponses().BadRequestResult("The board covers at most 31 days at a time.");

            var board = await _shop.GetRentalBoard(TenantId, from, to, categoryId);
            return new ApiResponses().OkResult(board);
        }

        // The signed-in rider's own rentals (lesson bikes booked online land here too), for the
        // My Passes page. Any authenticated user may read their own bookings.
        [HttpGet("Mine")]
        public async Task<IActionResult> Mine()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (UserId is not Guid me) return new ApiResponses().BadRequestResult("Please sign in.");
            var rentals = await _shop.ListRentalsForUser(me, TenantId, 50);
            return new ApiResponses().OkResult(rentals.Select(r => new
            {
                r.Id, r.StartsAt, r.EndsAt, r.Status, r.TotalCents, r.DepositCents,
                r.DepositCapturedCents, r.OrderNumber, r.EventId,
                Lines = r.Lines.Select(l => new { l.NameSnapshot, l.VariantLabel, l.Quantity }),
            }));
        }

        // Single rental, for the phone photo-capture page (scanned from the counter screen)
        // which needs to show WHICH rental it landed on before anyone starts shooting.
        [Authorize(Policy = TenantPermissions.Policy.ShopCounter)]
        [HttpGet("Rentals/{id:guid}")]
        public async Task<IActionResult> GetOne(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var rental = await _shop.GetRental(id, TenantId);
            return rental is null
                ? new ApiResponses().NotFoundResult("Rental not found.")
                : new ApiResponses().OkResult(rental);
        }

        [Authorize(Policy = TenantPermissions.Policy.ShopCounter)]
        [HttpGet("Rentals")]
        public async Task<IActionResult> List([FromQuery] bool activeOnly = true, [FromQuery] int limit = 100)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            return new ApiResponses().OkResult(await _shop.ListRentals(TenantId, activeOnly, Math.Clamp(limit, 1, 500)));
        }

        // The All Bookings screen: past and future, filtered and paged. The unpaged List above
        // stays for callers that want everything currently live in memory (the fleet schedule).
        //
        // scope defaults to upcoming because that is the counter's standing question; history is
        // one click away rather than the thing you wade through to find tomorrow's pickup.
        [Authorize(Policy = TenantPermissions.Policy.ShopCounter)]
        [HttpGet("Rentals/Page")]
        public async Task<IActionResult> SearchRentals(
            [FromQuery] string scope = "upcoming",
            [FromQuery] string? search = null,
            [FromQuery] string? statuses = null,
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 25)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");

            var parsedScope = scope?.ToLowerInvariant() switch
            {
                "past" => ShopRentalScope.Past,
                "all" => ShopRentalScope.All,
                "upcoming" or null or "" => ShopRentalScope.Upcoming,
                _ => (ShopRentalScope?)null,
            };
            if (parsedScope is null)
                return new ApiResponses().BadRequestResult("Scope must be upcoming, past, or all.");

            // Comma-separated on the wire; anything not a real rental status is rejected rather
            // than silently dropped, so a typo doesn't quietly widen the list.
            var wanted = (statuses ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => s.ToLowerInvariant())
                .Distinct()
                .ToList();
            var known = new[] { "pending", "paid", "out", "returned", "damaged", "cancelled", "failed" };
            var unknown = wanted.Where(s => !known.Contains(s)).ToList();
            if (unknown.Count > 0)
                return new ApiResponses().BadRequestResult($"Unknown rental status: {string.Join(", ", unknown)}.");

            if (from is not null && to is not null && to <= from)
                return new ApiResponses().BadRequestResult("The end of the date range must be after the start.");

            var result = await _shop.SearchRentals(TenantId, new ShopRentalQuery
            {
                Scope = parsedScope.Value,
                Search = search,
                Statuses = wanted,
                FromUtc = from?.ToUniversalTime(),
                ToUtc = to?.ToUniversalTime(),
                Page = page,
                PageSize = pageSize,
            });
            return new ApiResponses().OkResult(new { rows = result.Rows, total = result.Total });
        }

        [Authorize(Policy = TenantPermissions.Policy.ShopCounter)]
        [HttpPost("Rentals")]
        public async Task<IActionResult> Book([FromBody] BookShopRentalRequest req, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!_tenantContext.Tenant.BikeShopEnabled)
                return new ApiResponses().BadRequestResult("The bike shop isn't turned on for this track.");
            var startsAt = req.StartsAt.ToUniversalTime();
            var endsAt = req.EndsAt.ToUniversalTime();
            if (endsAt <= startsAt) return new ApiResponses().BadRequestResult("The return time must be after the start time.");
            if (startsAt < DateTime.UtcNow.AddHours(-1))
                return new ApiResponses().BadRequestResult("The rental window starts in the past.");

            // Billed in whole days: any part of a day counts as one (a 2-hour rental is 1 day).
            var days = Math.Max(1, (int)Math.Ceiling((endsAt - startsAt).TotalDays));

            var infos = (await _shop.GetVariantsForSale(req.Lines.Select(l => l.VariantId), TenantId))
                .ToDictionary(v => v.Id);
            var lines = new List<ShopRentalLine>();
            var usedItems = new HashSet<Guid>();
            int amount = 0, depositTotal = 0;

            foreach (var line in req.Lines)
            {
                var variant = await _shop.GetVariant(line.VariantId, TenantId);
                if (variant is null || !infos.ContainsKey(line.VariantId))
                    return new ApiResponses().BadRequestResult("An item in the booking is no longer available.");
                if (variant.DailyRateCents is null)
                    return new ApiResponses().BadRequestResult($"\"{infos[line.VariantId].ProductName}\" isn't set up for rental.");

                if (variant.TrackingKind == "serialized")
                {
                    if (line.ItemId is null)
                        return new ApiResponses().BadRequestResult($"Pick which \"{infos[line.VariantId].ProductName}\" unit is being rented.");
                    if (line.Quantity != 1)
                        return new ApiResponses().BadRequestResult("Serialized units are booked one per line.");
                    if (!usedItems.Add(line.ItemId.Value))
                        return new ApiResponses().BadRequestResult("The same unit is on the booking twice.");
                    var free = await _shop.GetFreeSerializedUnits(line.VariantId, TenantId, startsAt, endsAt);
                    if (!free.Any(u => u.Id == line.ItemId.Value))
                        return new ApiResponses().BadRequestResult(
                            $"That \"{infos[line.VariantId].ProductName}\" unit isn't free for this window.");
                }
                else
                {
                    var available = await _shop.GetPoolAvailability(line.VariantId, TenantId, startsAt, endsAt);
                    var alreadyInCart = lines.Where(l => l.VariantId == line.VariantId && l.ItemId == null).Sum(l => l.Quantity);
                    if (line.Quantity + alreadyInCart > available)
                        return new ApiResponses().BadRequestResult(
                            $"Only {available} of \"{infos[line.VariantId].ProductName}\" free for this window.");
                }

                var lineAmount = variant.DailyRateCents.Value * days * line.Quantity;
                amount += lineAmount;
                depositTotal += variant.DepositCents * line.Quantity;
                var info = infos[line.VariantId];
                var label = string.Join(" / ", new[] { info.Size, info.Color, info.Gender }.Where(s => !string.IsNullOrWhiteSpace(s)));
                lines.Add(new ShopRentalLine
                {
                    VariantId = line.VariantId,
                    ItemId = variant.TrackingKind == "serialized" ? line.ItemId : null,
                    Quantity = line.Quantity,
                    NameSnapshot = info.ProductName,
                    VariantLabel = string.IsNullOrWhiteSpace(label) ? null : label,
                    DailyRateCentsFrozen = variant.DailyRateCents.Value,
                    DepositCentsFrozen = variant.DepositCents,
                    LineAmountCents = lineAmount,
                });
            }

            // Season-pass rental benefit: a renter whose account (id, or email resolved to one)
            // holds a covering pass gets its 'rental' discount off the fee. Passes must be valid on
            // the rental's START date — a weekday pass discounts a Tuesday pickup, not a Saturday.
            var renterUserId = req.RenterUserId;
            if (renterUserId is null && !string.IsNullOrWhiteSpace(req.RenterEmail))
            {
                renterUserId = (await _users.GetByEmail(TenantId, req.RenterEmail.Trim()))?.Id;
            }
            var benefitDiscount = 0;
            if (renterUserId is not null && amount > 0)
            {
                var grants = await _seasonPasses.ListActiveBenefitGrantsForUser(
                    renterUserId.Value, TenantId, benefitType: "rental", scopeId: null, onDateUtc: startsAt);
                benefitDiscount = grants.Count == 0 ? 0 : grants.Max(g => g.Benefit.DiscountFor(amount));
            }

            // Rentals are all-in priced (no tax line for v1, matching the old rental system).
            // amount_cents keeps the gross; total_cents is what's actually charged.
            var netRental = amount - benefitDiscount;
            var tenant = _tenantContext.Tenant;

            // Damage waiver, if the renter took it and the track offers it. A non-refundable fee on
            // the GROSS rental that WAIVES the deposit; RentalCharge owns both halves of that rule
            // so the counter, the storefront, and packages cannot drift apart on it.
            var insuranceOffered = tenant.RentalInsuranceEnabled && tenant.RentalInsuranceBps > 0;
            var insuranceCents = Services.Payments.RentalCharge.InsuranceFor(
                amount, tenant.RentalInsuranceBps, insuranceOffered, req.Insurance);

            // Tenant service fee, same rate events use. The base is the discounted rental subtotal
            // (plus the waiver, which is revenue for a service) and NEVER the deposit: a refundable
            // deposit is the renter's own money held against damage, so taking a percentage of it
            // would charge them to lend us their deposit. ServiceChargeCents is what RidePass is
            // owed (the fee-calculator input); only the renter-paid share is added to what the card
            // is charged. A track that absorbs the fee (riderPaidBps = 0) still owes it.
            //
            // Computed by RentalCharge rather than inline so RentalChargeTests actually pins THIS
            // path: a test guarding a helper the app doesn't call cannot catch the regression it
            // describes. depositTotal is already summed across lines, each with its own per-unit
            // deposit, so there is no single (deposit, quantity) pair to hand over.
            var charge = Services.Payments.RentalCharge.WithInsurance(
                netRental, insuranceCents, tenant.ServiceChargeBps,
                tenant.RentalRiderPaidServiceChargeBps, depositTotal);
            var subtotal = netRental + insuranceCents;
            var serviceChargeCents = charge.ServiceChargeCents;
            var renterFeeCents = charge.RiderServiceChargeCents;
            // Taking the waiver replaces the deposit, so nothing is held.
            depositTotal = charge.DepositCents;

            // Sales tax. NULL rate = never configured, which we treat as 0 here and warn about in
            // the UI rather than guessing a rate. The taxable base is the rental (plus the renter
            // fee when the tenant says the fee is taxable) and NEVER the refundable deposit, which
            // is the renter's own money held against damage, not consideration for the rental.
            var taxableBase = subtotal + (tenant.RentalTaxServiceChargeTaxable ? renterFeeCents : 0);
            var taxCents = (int)Math.Round(
                (decimal)taxableBase * (tenant.RentalTaxBps ?? 0) / 10_000m, MidpointRounding.AwayFromZero);

            var total = subtotal + renterFeeCents + taxCents;

            var isCard = req.PaymentMethod == "card" && total > 0;
            if (isCard)
            {
                if (total < 50) return new ApiResponses().BadRequestResult("A card rental must be at least 50 cents.");
                if (tenant.StripeChargeMode == "direct" && string.IsNullOrEmpty(tenant.StripeConnectAccountId))
                    return new ApiResponses().BadRequestResult("This track charges on its own Stripe account but hasn't connected one yet.");
            }

            var rental = new ShopRental
            {
                TenantId = TenantId,
                RenterUserId = renterUserId,
                RenterName = string.IsNullOrWhiteSpace(req.RenterName) ? null : req.RenterName.Trim(),
                RenterEmail = string.IsNullOrWhiteSpace(req.RenterEmail) ? null : req.RenterEmail.Trim(),
                RenterPhone = string.IsNullOrWhiteSpace(req.RenterPhone) ? null : req.RenterPhone.Trim(),
                StartsAt = startsAt,
                EndsAt = endsAt,
                Status = "pending",
                // Gross INCLUDES the waiver fee, matching ShopStoreController, so amount_cents is
                // the whole thing charged for the rental before fee and tax. InsuranceCents below
                // is what lets it be itemised back out on a receipt or a refund.
                AmountCents = amount + insuranceCents,
                InsuranceCents = insuranceCents,
                InsuranceLabelSnapshot = insuranceCents > 0
                    ? (string.IsNullOrWhiteSpace(tenant.RentalInsuranceLabel)
                        ? "Damage Protection" : tenant.RentalInsuranceLabel.Trim())
                    : null,
                TaxCents = taxCents,
                TotalCents = total,
                ServiceChargeCents = serviceChargeCents,
                // A rental for N riders needs N signed waivers. Default from the largest single
                // line quantity: two bikes means two riders, while a bike + helmet is still one.
                RidersRequired = req.RidersRequired
                    ?? Math.Max(1, req.Lines.Count == 0 ? 1 : req.Lines.Max(l => l.Quantity)),
                DepositCents = depositTotal,
                PaymentMethod = isCard ? "stripe" : "cash",
                SoldByUserId = UserId,
            };
            var (rentalId, receipt) = await _shop.CreateRental(rental, lines);

            // ── Cash / $0: paid at the counter now. The deposit is recorded but not held. ─────
            if (!isCard)
            {
                if (await _shop.TryMarkRentalPaid(rentalId, TenantId))
                {
                    var orderNumber = await _shop.NextOrderNumber(TenantId);
                    await _shop.SetRentalOrderNumber(rentalId, orderNumber);
                    if (total > 0) await WriteCashLedger(rentalId, total, serviceChargeCents);
                    return new ApiResponses().OkResult(new
                    {
                        rentalId, receiptToken = receipt, status = "paid", orderNumber,
                        totalCents = total, depositCents = depositTotal, insuranceCents,
                    });
                }
                return new ApiResponses().OkResult(new { rentalId, receiptToken = receipt, status = "paid", totalCents = total });
            }

            // ── Card: fee PI (auto-capture) + optional deposit hold (manual capture). ─────────
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

                if (req.TakeDepositHold && depositTotal > 0)
                {
                    // Manual-capture hold, no application fee: a deposit is the rider's money under
                    // authorization, not revenue, so RidePass takes no cut of it.
                    var holdMeta = new Dictionary<string, string>(metadata) { ["sale_kind"] = "shop_rental_deposit_hold" };
                    var hold = await _payments.CreateHoldPaymentIntentAsync(depositTotal, "usd", holdMeta,
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
                totalCents = total, depositCents = depositTotal, insuranceCents,
            });
        }

        [Authorize(Policy = TenantPermissions.Policy.ShopCounter)]
        [HttpPost("Rentals/{id:guid}/CheckOut")]
        public async Task<IActionResult> CheckOut(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var rental = await _shop.GetRental(id, TenantId);
            if (rental is null) return new ApiResponses().NotFoundResult("Rental not found.");

            // Gear does not leave the counter unsigned. Both documents are checked: the track's
            // liability waiver and the shop's rental agreement. Enforced HERE rather than at
            // booking because a rental can be booked online, and the signature is collected at
            // pickup (or remotely beforehand).
            var readiness = await GetReadiness(rental);
            if (!readiness.CanCheckOut)
            {
                var missing = new List<string>();
                if (readiness.AgreementRequired && !readiness.AgreementSigned) missing.Add("the rental agreement");
                if (readiness.WaiverRequired && !readiness.WaiverSigned) missing.Add($"the waiver ({readiness.RidersOutstanding} of {readiness.RidersRequired} rider(s) still unsigned)");
                return new ApiResponses().BadRequestResult(
                    $"{rental.RenterName ?? "The renter"} still needs to sign {string.Join(" and ", missing)} before the gear goes out.");
            }

            var ok = await _shop.CheckOutRental(id, TenantId, UserId);
            return ok
                ? new ApiResponses().OkResult()
                : new ApiResponses().BadRequestResult("This rental can't be checked out — it must be paid first (and not already out).");
        }

        /// <summary>Emails the renter a link to sign the agreement and waiver themselves, for
        /// rentals booked online where nobody is standing at the counter. The link carries a
        /// per-rental token and can only reach the signing page.</summary>
        [Authorize(Policy = TenantPermissions.Policy.ShopCounter)]
        [HttpPost("Rentals/{id:guid}/SendSigningLink")]
        public async Task<IActionResult> SendSigningLink(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var rental = await _shop.GetRental(id, TenantId);
            if (rental is null) return new ApiResponses().NotFoundResult("Rental not found.");
            if (rental.Status is "out" or "returned" or "damaged" or "cancelled" or "failed")
                return new ApiResponses().BadRequestResult("This rental is already closed.");
            if (string.IsNullOrWhiteSpace(rental.RenterEmail))
                return new ApiResponses().BadRequestResult("This rental has no renter email. Add one first.");
            if (!_emailer.IsConfigured)
                return new ApiResponses().BadRequestResult("Email isn't set up on this server.");

            var tenant = _tenantContext.Tenant;
            var apex = _config["App:RootDomain"] ?? "ridepass.io";
            var link = $"https://{tenant.Subdomain}.{apex}/SignRental/{rental.SignatureRequestToken}";
            static string Enc(string s) => System.Net.WebUtility.HtmlEncode(s);
            var gear = rental.Lines.Count > 0
                ? string.Join(", ", rental.Lines.Select(l => l.NameSnapshot).Distinct())
                : "your rental";
            var html =
                $"<div style=\"font-family:Arial,Helvetica,sans-serif;max-width:480px\">" +
                $"<h2 style=\"margin:0 0 8px\">{Enc(tenant.DisplayName)}</h2>" +
                $"<p>Hi {Enc(rental.RenterName ?? "there")},</p>" +
                $"<p>Before you pick up {Enc(gear)}, please sign the rental agreement and waiver. " +
                $"It only takes a minute, and it means you can collect your gear without paperwork " +
                $"at the counter:</p>" +
                $"<p style=\"margin:16px 0\"><a href=\"{link}\" style=\"background:#1976d2;color:#fff;padding:10px 18px;" +
                $"border-radius:6px;text-decoration:none\">Sign now</a></p>" +
                $"<p style=\"font-size:12px;color:#666\">Or paste this link into your browser:<br/>{link}</p></div>";
            if (!await _emailer.Send(rental.RenterEmail!, $"{tenant.DisplayName}: sign before you ride",
                    html, null, Services.Email.TenantEmailIdentity.For(tenant)))
                return new ApiResponses().BadRequestResult("Could not send the email. Check the address and try again.");

            await _shop.MarkRentalSignatureRequestSent(id, TenantId);
            return new ApiResponses().OkResult();
        }

        /// <summary>Captures the track's liability waiver at the counter. Walk-in renters have no
        /// account, so this writes the shared rider_waiver_signature store with the attendee's
        /// details (the same path event registration uses) and links it to the rental. Without
        /// this a walk-in who hasn't signed elsewhere is simply stuck: the checkout gate blocks
        /// them and the counter has no way to resolve it.</summary>
        [Authorize(Policy = TenantPermissions.Policy.ShopCounter)]
        [HttpPost("Rentals/{id:guid}/SignWaiver")]
        public async Task<IActionResult> SignWaiver(Guid id, [FromBody] SignRentalWaiverRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var rental = await _shop.GetRental(id, TenantId);
            if (rental is null) return new ApiResponses().NotFoundResult("Rental not found.");

            var waiver = await _waivers.GetActive(TenantId);
            if (waiver is null)
                return new ApiResponses().BadRequestResult("This track has no active waiver to sign.");

            if (string.IsNullOrWhiteSpace(req.SignatureDataUrl))
                return new ApiResponses().BadRequestResult("A signature is required.");
            if (string.IsNullOrWhiteSpace(req.FirstName) || string.IsNullOrWhiteSpace(req.LastName))
                return new ApiResponses().BadRequestResult("Enter the rider's first and last name.");
            if (req.SignedByParent && string.IsNullOrWhiteSpace(req.ParentName))
                return new ApiResponses().BadRequestResult("Enter the parent or guardian's name.");

            var signatureId = await _waivers.SignRegistrant(
                TenantId, waiver.Id,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                req.SignatureDataUrl,
                signerEmail: string.IsNullOrWhiteSpace(req.Email) ? rental.RenterEmail : req.Email.Trim(),
                signerName: req.SignedByParent ? req.ParentName!.Trim() : $"{req.FirstName.Trim()} {req.LastName.Trim()}",
                attendeeFirstName: req.FirstName.Trim(),
                attendeeLastName: req.LastName.Trim(),
                attendeeBirthdate: req.Birthdate,
                signedByParent: req.SignedByParent,
                parentName: req.ParentName?.Trim(),
                parentPhone: req.ParentPhone?.Trim());

            await _shop.AddRentalWaiverSignature(id, TenantId, signatureId);
            // Report progress so the counter knows whether more riders still owe a signature.
            var after = await GetReadiness(rental);
            return new ApiResponses().OkResult(new
            {
                signatureId,
                ridersSigned = after.RidersSigned,
                ridersRequired = after.RidersRequired,
                ridersOutstanding = after.RidersOutstanding,
            });
        }

        /// <summary>What this rental still needs before hand-over, so the counter can show it up
        /// front instead of only failing at the moment someone presses Check out.</summary>
        [Authorize(Policy = TenantPermissions.Policy.ShopCounter)]
        [HttpGet("Rentals/{id:guid}/Readiness")]
        public async Task<IActionResult> Readiness(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var rental = await _shop.GetRental(id, TenantId);
            if (rental is null) return new ApiResponses().NotFoundResult("Rental not found.");
            return new ApiResponses().OkResult(await GetReadiness(rental));
        }

        private async Task<RentalCheckoutReadiness> GetReadiness(ShopRentalWithLines rental)
        {
            var result = new RentalCheckoutReadiness();

            // Rental agreement: required whenever the tenant has one published. The signature has
            // to be against the CURRENT version, so re-publishing new terms re-asks.
            var agreement = await _shop.GetActiveAgreement(TenantId, "rental_agreement");
            result.AgreementRequired = agreement is not null;
            if (result.AgreementRequired)
                result.AgreementSigned = await _shop.HasCurrentAgreementSignature(rental.Id, TenantId, "rental_agreement");

            // Waiver: the track's own, if they have an active one. A rental already carries a
            // signature id when one was collected at booking; otherwise look the renter up, by
            // account when we have one and by email for a walk-in.
            // Waiver: one signature PER RIDER. Counting rows in shop_rental_waiver rather than
            // checking a single column is the whole point — a rental for three riders needs three.
            //
            // Note there is deliberately no "the renter already has a signature on file" fallback
            // any more. That shortcut is what let a parent's own waiver stand in for three kids;
            // it also can't tell you WHICH riders are covered. Every rider signs, at the counter or
            // via the signing link.
            var waiver = await _waivers.GetActive(TenantId);
            result.WaiverRequired = waiver is not null;
            result.RidersRequired = Math.Max(1, rental.RidersRequired);
            if (waiver is not null)
            {
                result.Signers = await _shop.ListRentalWaiverSigners(rental.Id, TenantId);
                result.RidersSigned = result.Signers.Count;
            }
            return result;
        }

        [Authorize(Policy = TenantPermissions.Policy.ShopCounter)]
        [HttpPost("Rentals/{id:guid}/Return")]
        public async Task<IActionResult> Return(Guid id, [FromBody] ReturnShopRentalRequest req, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var rental = await _shop.GetRental(id, TenantId);
            if (rental is null) return new ApiResponses().NotFoundResult("Rental not found.");
            if (rental.Status != "out")
                return new ApiResponses().BadRequestResult("Only a checked-out rental can be returned.");

            var captured = Math.Max(0, Math.Min(req.DepositCapturedCents, rental.DepositCents));
            var isDirect = !string.IsNullOrEmpty(rental.StripeConnectedAccountId);

            // Settle the deposit hold FIRST: if Stripe refuses, the rental stays 'out' so staff can
            // retry rather than the money silently staying authorized on a "returned" rental.
            if (!string.IsNullOrEmpty(rental.DepositPiId))
            {
                try
                {
                    if (captured > 0)
                    {
                        var status = await _payments.CapturePaymentIntentAsync(rental.DepositPiId!, captured,
                            isDirect ? rental.StripeConnectedAccountId : null, ct);
                        if (status != "succeeded")
                            return new ApiResponses().BadRequestResult(
                                $"The deposit capture didn't go through (status '{status ?? "unknown"}'). Try again.");
                        await WriteDepositLedger(rental, captured, isDirect);
                    }
                    else
                    {
                        await _payments.CancelPaymentIntentAsync(rental.DepositPiId!,
                            isDirect ? rental.StripeConnectedAccountId : null, ct);
                    }
                }
                catch (Exception ex)
                {
                    return new ApiResponses().BadRequestResult($"Could not settle the deposit hold: {ex.Message}");
                }
            }

            var ok = await _shop.ReturnRental(id, TenantId, UserId, damaged: captured > 0, captured,
                string.IsNullOrWhiteSpace(req.ConditionNotes) ? null : req.ConditionNotes.Trim());
            return ok
                ? new ApiResponses().OkResult(new { depositCapturedCents = captured })
                : new ApiResponses().BadRequestResult("Could not mark the rental returned. Reload and try again.");
        }

        [Authorize(Policy = TenantPermissions.Policy.ShopCounter)]
        [HttpPost("Rentals/{id:guid}/Cancel")]
        public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var rental = await _shop.GetRental(id, TenantId);
            if (rental is null) return new ApiResponses().NotFoundResult("Rental not found.");
            if (rental.Status is not ("pending" or "paid"))
                return new ApiResponses().BadRequestResult("Only a rental that hasn't been picked up can be cancelled.");

            // Release the deposit hold; an unconfirmed fee PI simply expires at Stripe. A PAID fee
            // is not auto-refunded here — refunds are a deliberate action, not a cancel side effect.
            if (!string.IsNullOrEmpty(rental.DepositPiId))
            {
                try
                {
                    await _payments.CancelPaymentIntentAsync(rental.DepositPiId!,
                        string.IsNullOrEmpty(rental.StripeConnectedAccountId) ? null : rental.StripeConnectedAccountId, ct);
                }
                catch { /* already cancelled/expired at Stripe is fine */ }
            }
            var n = await _shop.CancelRental(id, TenantId);
            return n == 0
                ? new ApiResponses().BadRequestResult("Could not cancel — the rental may have just been checked out.")
                : new ApiResponses().OkResult();
        }

        // Cash convention shared with the register: tenant holds the drawer cash, so net = -cut.
        private async Task WriteCashLedger(Guid rentalId, int totalCents, int serviceChargeCents)
        {
            try
            {
                var calc = await _feeCalculator.Calculate(TenantId, totalCents, 0, serviceChargeCents, DateTime.UtcNow);
                await _ledger.Insert(new TenantLedgerEntry
                {
                    TenantId = TenantId,
                    EntryKind = "sale",
                    SourceKind = "shop_rental",
                    SourceId = rentalId,
                    OccurredAtUtc = DateTime.UtcNow,
                    GrossCents = totalCents,
                    StripeFeeCents = 0,
                    RidepassCutCents = calc.RidepassCutCents,
                    NetToTenantCents = -calc.RidepassCutCents,
                    PaymentMethod = "cash",
                    SoldByUserId = UserId,
                    Memo = "Bike shop rental, cash",
                });
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505") { /* idempotent */ }
        }

        // Damage kept out of the deposit becomes tenant revenue. Distinct source kind so it can
        // coexist with the rental fee's own sale entry on the same rental id.
        private async Task WriteDepositLedger(ShopRental rental, int capturedCents, bool isDirect)
        {
            try
            {
                var stripeFee = isDirect ? 0 : (await _payments.GetActualStripeFeeCentsAsync(rental.DepositPiId!,
                    rental.StripeConnectedAccountId) ?? 0);
                var calc = await _feeCalculator.Calculate(TenantId, capturedCents, stripeFee, 0, DateTime.UtcNow, isDirect);
                await _ledger.Insert(new TenantLedgerEntry
                {
                    TenantId = TenantId,
                    EntryKind = "sale",
                    SourceKind = "shop_rental_deposit",
                    SourceId = rental.Id,
                    OccurredAtUtc = DateTime.UtcNow,
                    GrossCents = capturedCents,
                    StripeFeeCents = stripeFee,
                    RidepassCutCents = calc.RidepassCutCents,
                    NetToTenantCents = calc.NetToTenantCents,
                    StripePaymentIntentId = rental.DepositPiId,
                    PaymentMethod = isDirect ? "stripe_direct" : "stripe",
                    SoldByUserId = UserId,
                    Memo = "Damage captured from rental deposit",
                });
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505") { /* idempotent */ }
        }
    }
}
