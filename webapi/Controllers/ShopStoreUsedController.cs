using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.BikeShop;
using Services.Helpers;
using Services.Helpers.Interfaces;
using Services.Payments;
using Services.Repositories.Data.BikeShopData;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Interfaces;
using webapi.Controllers.API.Data.BikeShop;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    // The rider-facing side of used & ex-rental listings (Script0302): browse anonymously, buy
    // signed-in, pick up at the shop. An order here is an ordinary shop_sale (channel 'online')
    // with ONE line that names the unit, created exactly the way ShopStoreController.Order and
    // the register create theirs, so the generic StripePurchaseFinalizer settles it untouched:
    // it finds the sale by its PaymentIntent, marks it paid, assigns the order number, depletes
    // (which flips the unit to 'sold' through the line's item_id), emails the pickup claim and
    // books the ledger. Nothing in this controller runs after payment.
    //
    // What is different from a cart of pool goods: one bike, one buyer. A checkout in progress
    // holds the unit for twenty minutes (reserved_sale_id / reserved_at on the listing, live-ness
    // decided by ListingRules) under a per-listing advisory lock, so two riders cannot both reach
    // Stripe for the same bike. No coupons, gift cards or store credit on a used unit in v1.
    [ApiController]
    [Route("api/[controller]")]
    public class ShopStoreUsedController : ControllerBase
    {
        private readonly IShopListingRepository _listings;
        private readonly IBikeShopRepository _shop;
        private readonly IListingReservationService _reservations;
        private readonly IUserRepository _users;
        private readonly IChargeRouter _chargeRouter;
        private readonly IPaymentProvider _payments;
        private readonly IDbHelper _db;
        private readonly ITenantContext _tenantContext;

        public ShopStoreUsedController(IShopListingRepository listings, IBikeShopRepository shop,
            IListingReservationService reservations, IUserRepository users,
            IChargeRouter chargeRouter, IPaymentProvider payments, IDbHelper db, ITenantContext tenantContext)
        {
            _listings = listings;
            _shop = shop;
            _reservations = reservations;
            _users = users;
            _chargeRouter = chargeRouter;
            _payments = payments;
            _db = db;
            _tenantContext = tenantContext;
        }

        private Guid TenantId => _tenantContext.TenantId;

        /// <summary>
        /// Everything on the floor right now (active, published, not held by a live checkout), plus
        /// the ids of catalog products flagged 'used' so the shop page can badge them without a
        /// second catalog shape.
        /// </summary>
        [HttpGet("Listings")]
        public async Task<IActionResult> Listings()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!_tenantContext.Tenant.BikeShopEnabled)
                return new ApiResponses().OkResult(new { listings = Array.Empty<object>(), usedProductIds = Array.Empty<Guid>() });

            var now = DateTime.UtcNow;
            var rows = await _listings.ListPublic(TenantId);
            var listings = rows
                .Where(r => !ListingRules.IsReservationLive(r.ReservedSaleId, r.ReservedAt, r.ReservedSaleStatus, now))
                .Select(r => ToPublic(r, reserved: false))
                .ToList();
            var usedProductIds = await _listings.ListUsedProductIds(TenantId);
            return new ApiResponses().OkResult(new { listings, usedProductIds });
        }

        /// <summary>One listing's page. 404 unless it is active and published; a listing held by
        /// someone else's checkout is still returned, with reserved = true, so the page can say so.</summary>
        [HttpGet("Listings/{id:guid}")]
        public async Task<IActionResult> Listing(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!_tenantContext.Tenant.BikeShopEnabled) return new ApiResponses().NotFoundResult("Listing not found.");
            var row = await _listings.Get(id, TenantId);
            if (row is null || !IsOnTheFloor(row)) return new ApiResponses().NotFoundResult("Listing not found.");
            var live = ListingRules.IsReservationLive(row.ReservedSaleId, row.ReservedAt, row.ReservedSaleStatus, DateTime.UtcNow);
            return new ApiResponses().OkResult(ToPublic(row, reserved: live));
        }

        /// <summary>
        /// Buy this bike, pick it up at the shop. Mirrors ShopStoreController.Order for one line:
        /// same tax treatment, same platform charge, same PaymentIntent routing, same response
        /// shape ({ saleId, receiptToken, status, clientSecret, totalCents, dueCents, ... }).
        /// </summary>
        [Authorize]
        [HttpPost("Listings/{id:guid}/Order")]
        public async Task<IActionResult> Order(Guid id, [FromBody] UsedListingOrderRequest? req, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var tenant = _tenantContext.Tenant;
            if (!tenant.BikeShopEnabled)
                return new ApiResponses().BadRequestResult("The shop isn't available at this track.");
            if (!Guid.TryParse(User.FindFirst("UserId")?.Value, out var userId))
                return new ApiResponses().BadRequestResult("Not signed in.");
            var user = await _users.GetById(userId);
            if (user is null) return new ApiResponses().BadRequestResult("User not found.");
            if (req is not null && (!string.IsNullOrWhiteSpace(req.CouponCode)
                                    || !string.IsNullOrWhiteSpace(req.GiftCardCode) || req.CreditCents > 0))
                return new ApiResponses().BadRequestResult(
                    "Coupons, gift cards and store credit can't be applied to a used bike yet; it's priced as marked.");
            if (tenant.StripeChargeMode == "direct" && string.IsNullOrEmpty(tenant.StripeConnectAccountId))
                return new ApiResponses().BadRequestResult("This track can't take online payments yet.");

            // One checkout at a time per listing: the hold check and the hold itself must not
            // interleave with another rider's.
            await using var listingLock = await _db.AcquireAdvisoryLock($"used-listing:{id}");

            var listing = await _listings.Get(id, TenantId);
            if (listing is null || !IsOnTheFloor(listing))
                return new ApiResponses().NotFoundResult("This bike is no longer for sale.");

            var now = DateTime.UtcNow;
            if (ListingRules.IsReservationLive(listing.ReservedSaleId, listing.ReservedAt, listing.ReservedSaleStatus, now))
            {
                // Which case it is travels as data.reason, so the page can offer to resume rather
                // than just show the copy.
                if (ListingRules.HoldReason(listing.ReservedBuyerUserId, userId) == ListingRules.HeldByYou)
                    return new ApiResponses().ConflictResult(
                        "You already started checking out this bike. Finish that payment, or try again in a few minutes.",
                        await ResumeFor(listing, userId, ct));
                return new ApiResponses().ConflictResult(HeldByOtherMessage, new { reason = ListingRules.HeldByOther });
            }
            if (listing.ReservedSaleId is not null)
            {
                // A stale hold is overwritten, but only once its checkout can never pay.
                var release = await _reservations.Release(listing, ct);
                if (release == ReservationRelease.AlreadyPaid)
                    return new ApiResponses().ConflictResult("This bike was just sold.", new { reason = "sold" });
                if (release == ReservationRelease.Blocked)
                    return new ApiResponses().ConflictResult(HeldByOtherMessage, new { reason = ListingRules.HeldByOther });
            }

            // Tax rate (the product's category, else the tenant default) and the frozen label come
            // from the same query the register and the store price with. The asking price is the
            // listing's, not the variant's: a fleet bike usually has no retail price at all.
            var info = (await _shop.GetVariantsForSale(new[] { listing.VariantId }, TenantId)).FirstOrDefault();
            if (info is null)
                return new ApiResponses().BadRequestResult("This bike is no longer available. Refresh and try again.");
            var item = await _shop.GetItem(listing.ItemId, TenantId);
            if (item is null || item.Status != ListingRules.ForSale)
                return new ApiResponses().NotFoundResult("This bike is no longer for sale.");

            var subtotal = listing.AskingPriceCents;
            var line = new ShopSaleLine
            {
                VariantId = listing.VariantId,
                ItemId = listing.ItemId,
                Quantity = 1,
                NameSnapshot = listing.Title,
                VariantLabel = ListingRules.VariantLabel(info.Size, info.Color, info.Gender),
                UnitPriceCents = subtotal,
                DiscountCents = 0,
                TaxRateBps = info.TaxRateBps,
                UnitCostCentsFrozen = item.AcquiredCostCents ?? info.CostCents,
            };
            // Tax-exclusive, like every online order (the sale is written PricesIncludeTax false).
            line.TaxCents = info.TaxRateBps > 0
                ? (int)Math.Round(subtotal * info.TaxRateBps / 10000.0, MidpointRounding.AwayFromZero) : 0;
            var taxTotal = line.TaxCents;

            // Same platform charge the counter and the store take, computed the same way.
            var (shopServiceCharge, buyerFee) = ServiceChargeSplit.Compute(
                subtotal, tenant.ServiceChargeBps, tenant.ShopBuyerPaidServiceChargeBps);
            var feeTaxCents = 0;
            if (buyerFee > 0 && tenant.ShopTaxServiceChargeTaxable)
            {
                var defaultRate = (await _shop.ListTaxCategories(TenantId, activeOnly: true))
                    .FirstOrDefault(c => c.IsDefault)?.RateBps;
                feeTaxCents = ShopFeeTax.Compute(buyerFee, taxable: true, defaultRate, pricesIncludeTax: false);
                taxTotal += feeTaxCents;
            }
            var total = subtotal + buyerFee + taxTotal;
            var due = total;
            if (due < 50)
                return new ApiResponses().BadRequestResult("The total is below the card minimum. Buy this one at the counter.");

            var sale = new ShopSale
            {
                TenantId = TenantId,
                BuyerUserId = userId,
                BuyerEmail = user.Email,
                BuyerName = $"{user.FirstName} {user.LastName}".Trim(),
                Status = "pending",
                SubtotalCents = subtotal,
                DiscountCents = 0,
                TaxCents = taxTotal,
                TotalCents = total,
                ServiceChargeCents = shopServiceCharge,
                CreditAppliedCents = 0,
                PricesIncludeTax = false,
                PaymentMethod = "stripe",
                OrderChannel = "online",
            };
            Guid saleId, receipt;
            try
            {
                (saleId, receipt) = await _shop.CreateSale(sale, new[] { line });
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505" && ex.ConstraintName == "uk_shop_sale_line_item")
            {
                // Some other sale (the register, a checkout that bypassed the hold) already names this unit.
                return new ApiResponses().ConflictResult("This bike is already on another order.", new { reason = "unavailable" });
            }

            if (!await _listings.Reserve(listing.Id, TenantId, saleId))
            {
                // Taken down between the read and the hold: nothing holds the unit for this sale,
                // so there is no reservation to release; the sale just dies and lets go of the unit.
                await _listings.MarkSaleDead(saleId, TenantId, "abandoned");
                await _listings.DetachDeadSaleLine(saleId, listing.ItemId, TenantId);
                return new ApiResponses().NotFoundResult("This bike is no longer for sale.");
            }

            var metadata = new Dictionary<string, string>
            {
                ["tenant_id"] = TenantId.ToString(),
                ["sale_kind"] = "shop_sale",
                ["shop_sale_id"] = saleId.ToString(),
                ["used_listing_id"] = listing.Id.ToString(),
            };
            PaymentIntentCreated intent;
            ChargePlan plan;
            try
            {
                plan = _chargeRouter.Plan(tenant, serviceFeeCents: shopServiceCharge, chargeAmountCents: due);
                intent = await _payments.CreatePaymentIntentAsync(due, "usd", metadata, user.Email,
                    connectedAccountId: plan.ConnectedAccountId, applicationFeeCents: plan.ApplicationFeeCents, ct: ct);
            }
            catch (InvalidOperationException ex)
            {
                // Nothing was charged and no intent exists: release this checkout's own hold the one
                // way holds are released (sale 'failed', line detached, hold cleared).
                await _reservations.Release(listing.Id, listing.ItemId, TenantId, saleId, "failed", ct);
                return new ApiResponses().BadRequestResult(ex.Message);
            }
            await _listings.SetSalePaymentIntent(saleId, TenantId, intent.IntentId);
            if (plan.IsDirect) await _shop.MarkSaleDirectCharge(saleId, TenantId, plan.ConnectedAccountId!);

            return new ApiResponses().OkResult(new
            {
                saleId,
                receiptToken = receipt,
                status = "pending",
                clientSecret = intent.ClientSecret,
                paymentIntentId = intent.IntentId,
                totalCents = total,
                subtotalCents = subtotal,
                taxCents = taxTotal,
                feeCents = buyerFee,
                discountCents = 0,
                creditAppliedCents = 0,
                dueCents = due,
                listingId = listing.Id,
                title = listing.Title,
                reservedUntilUtc = DateTime.SpecifyKind(now.AddMinutes(ListingRules.ReservationMinutes), DateTimeKind.Utc),
            });
        }

        private const string HeldByOtherMessage = "Someone else is checking out this bike right now.";

        /// <summary>
        /// The 409 data for a rider whose own checkout holds the bike: the sale, and the client
        /// secret of its PaymentIntent when that intent is still waiting on them, so the page can
        /// mount the same Payment Element again instead of starting a second order. The secret only
        /// ever goes back to the buyer the sale belongs to.
        /// </summary>
        private async Task<object> ResumeFor(ShopListingRow listing, Guid userId, CancellationToken ct)
        {
            var sale = await _shop.GetSale(listing.ReservedSaleId!.Value, TenantId);
            string? clientSecret = null;
            string? paymentStatus = null;
            if (sale is not null && sale.BuyerUserId == userId && sale.Status == "pending"
                && !string.IsNullOrEmpty(sale.StripePaymentIntentId))
            {
                try
                {
                    var pi = await _payments.GetPaymentIntentSnapshotAsync(
                        sale.StripePaymentIntentId, sale.StripeConnectedAccountId, ct);
                    paymentStatus = pi?.Status;
                    if (pi is not null && ListingRules.IsResumable(pi.Status)) clientSecret = pi.ClientSecret;
                }
                catch (InvalidOperationException) { /* Stripe unreachable: resume is a convenience */ }
            }
            var mine = sale is not null && sale.BuyerUserId == userId;
            return new
            {
                reason = ListingRules.HeldByYou,
                saleId = mine ? sale!.Id : (Guid?)null,
                clientSecret,
                paymentStatus,
                saleStatus = mine ? sale!.Status : null,
                totalCents = mine ? sale!.TotalCents : (int?)null,
                dueCents = mine ? sale!.TotalCents - sale.CreditAppliedCents : (int?)null,
                reservedUntilUtc = listing.ReservedSaleStatus == "paid"
                    ? null
                    : ListingRules.ReservationExpiresAtUtc(listing.ReservedAt) is DateTime until
                        ? DateTime.SpecifyKind(until, DateTimeKind.Utc) : (DateTime?)null,
            };
        }

        // ── Shapes ────────────────────────────────────────────────────────────────
        private static bool IsOnTheFloor(ShopListingRow r) =>
            r.UnlistedAt is null && r.Published && r.ItemStatus == ListingRules.ForSale
            && r.ProductIsActive && r.VariantIsActive;

        /// <summary>PublicListingDto (docs: USED_SALES contract). No costs, no serials, no who-listed-it.</summary>
        private static object ToPublic(ShopListingRow r, bool reserved)
        {
            var photos = r.Photos.Select(p => new { url = p.ImageUrl, caption = p.Caption }).ToList();
            return new
            {
                id = r.Id,
                title = r.Title,
                productName = r.ProductName,
                variantLabel = ListingRules.VariantLabel(r.Size, r.Color, r.Gender),
                categoryName = r.CategoryName,
                askingPriceCents = r.AskingPriceCents,
                conditionGrade = r.ConditionGrade,
                conditionNotes = r.ConditionNotes,
                photos,
                coverUrl = photos.FirstOrDefault()?.url ?? r.ProductImageUrl,
                specs = new { size = r.Size, color = r.Color },
                reserved,
                listedAtUtc = DateTime.SpecifyKind(r.ListedAt, DateTimeKind.Utc),
            };
        }
    }
}
