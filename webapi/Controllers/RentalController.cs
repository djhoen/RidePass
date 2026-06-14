using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Coupons;
using Services.GiftCards;
using Services.Helpers;
using Services.Payments;
using Services.Repositories.Data.CouponData;
using Services.Repositories.Data.GiftCardData;
using Services.Repositories.Data.RentalData;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Rental;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RentalController : ControllerBase
    {
        private readonly IRentalRepository _rentals;
        private readonly IUserRepository _users;
        private readonly IWaiverRepository _waivers;
        private readonly IPaymentProvider _payments;
        private readonly ICouponRepository _coupons;
        private readonly ICouponValidator _couponValidator;
        private readonly IGiftCardRepository _giftCards;
        private readonly IGiftCardValidator _giftCardValidator;
        private readonly ITenantContext _tenantContext;

        public RentalController(
            IRentalRepository rentals,
            IUserRepository users,
            IWaiverRepository waivers,
            IPaymentProvider payments,
            ICouponRepository coupons,
            ICouponValidator couponValidator,
            IGiftCardRepository giftCards,
            IGiftCardValidator giftCardValidator,
            ITenantContext tenantContext)
        {
            _rentals = rentals;
            _users = users;
            _waivers = waivers;
            _payments = payments;
            _coupons = coupons;
            _couponValidator = couponValidator;
            _giftCards = giftCards;
            _giftCardValidator = giftCardValidator;
            _tenantContext = tenantContext;
        }

        // ── Products: public list ─────────────────────────────────────────────
        [HttpGet("Products")]
        public async Task<IActionResult> ListActive()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!_tenantContext.Tenant.RentalsEnabled) return new ApiResponses().OkResult(new List<RentalProductResponse>());
            var products = await _rentals.ListProducts(_tenantContext.TenantId, activeOnly: true);
            var responses = new List<RentalProductResponse>();
            foreach (var p in products) responses.Add(await ToResponse(p, includeItemCounts: false));
            return new ApiResponses().OkResult(responses);
        }

        // ── Products: tenant-admin CRUD ───────────────────────────────────────
        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpGet("Products/Admin")]
        public async Task<IActionResult> ListForAdmin()
        {
            var products = await _rentals.ListProducts(_tenantContext.TenantId, activeOnly: false);
            var responses = new List<RentalProductResponse>();
            foreach (var p in products) responses.Add(await ToResponse(p, includeItemCounts: true));
            return new ApiResponses().OkResult(responses);
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost("Products")]
        public async Task<IActionResult> CreateProduct([FromBody] UpsertRentalProductRequest req)
        {
            var validate = ValidateProduct(req);
            if (validate is not null) return validate;

            var product = new RentalProduct
            {
                TenantId = _tenantContext.TenantId,
                Name = req.Name.Trim(),
                Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
                ImageUrl = string.IsNullOrWhiteSpace(req.ImageUrl) ? null : req.ImageUrl.Trim(),
                DailyRateCents = req.DailyRateCents,
                DepositCents = req.DepositCents,
                TrackingKind = req.TrackingKind,
                InventoryPool = req.TrackingKind == "pool" ? req.InventoryPool : null,
                RequiresWaiver = req.RequiresWaiver,
                RiderPaidServiceChargeBps = req.RiderPaidServiceChargeBps,
                IsActive = req.IsActive,
                SortOrder = req.SortOrder,
            };
            product.Id = await _rentals.CreateProduct(product);
            return new ApiResponses().OkResult(await ToResponse(product, includeItemCounts: true));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPut("Products/{id:guid}")]
        public async Task<IActionResult> UpdateProduct(Guid id, [FromBody] UpsertRentalProductRequest req)
        {
            var existing = await _rentals.GetProduct(id, _tenantContext.TenantId);
            if (existing is null) return new ApiResponses().NotFoundResult("Rental product not found.");

            var validate = ValidateProduct(req);
            if (validate is not null) return validate;

            // Prevent flipping tracking_kind once a product has been booked — the per-item
            // assignment table doesn't translate cleanly between modes.
            // (Soft check: only block when changing kind. Tenant can still adjust other fields.)
            if (existing.TrackingKind != req.TrackingKind)
            {
                return new ApiResponses().BadRequestResult(
                    "Tracking kind can't be changed after creation. Make a new product if you need to switch between pool and per-item tracking.");
            }

            existing.Name = req.Name.Trim();
            existing.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
            existing.ImageUrl = string.IsNullOrWhiteSpace(req.ImageUrl) ? null : req.ImageUrl.Trim();
            existing.DailyRateCents = req.DailyRateCents;
            existing.DepositCents = req.DepositCents;
            existing.InventoryPool = req.TrackingKind == "pool" ? req.InventoryPool : null;
            existing.RequiresWaiver = req.RequiresWaiver;
            existing.RiderPaidServiceChargeBps = req.RiderPaidServiceChargeBps;
            existing.IsActive = req.IsActive;
            existing.SortOrder = req.SortOrder;
            await _rentals.UpdateProduct(existing);
            return new ApiResponses().OkResult(await ToResponse(existing, includeItemCounts: true));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost("Products/Reorder")]
        public async Task<IActionResult> ReorderProducts([FromBody] ReorderRentalProductsRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (req.Items.Count == 0) return new ApiResponses().OkResult();
            var ids = req.Items.Select(i => i.Id).ToList();
            var orders = req.Items.Select(i => i.SortOrder).ToList();
            await _rentals.UpdateProductSortOrders(_tenantContext.TenantId, ids, orders);
            return new ApiResponses().OkResult();
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpDelete("Products/{id:guid}")]
        public async Task<IActionResult> DeleteProduct(Guid id)
        {
            var existing = await _rentals.GetProduct(id, _tenantContext.TenantId);
            if (existing is null) return new ApiResponses().NotFoundResult("Rental product not found.");
            try
            {
                await _rentals.DeleteProduct(id, _tenantContext.TenantId);
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23503")
            {
                return new ApiResponses().BadRequestResult(
                    "This rental has bookings on file and can't be deleted. Set inactive instead.");
            }
            return new ApiResponses().OkResult();
        }

        // ── Per-item units (admin) ────────────────────────────────────────────
        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpGet("Products/{productId:guid}/Items")]
        public async Task<IActionResult> ListItems(Guid productId)
        {
            var product = await _rentals.GetProduct(productId, _tenantContext.TenantId);
            if (product is null) return new ApiResponses().NotFoundResult("Rental product not found.");
            var items = await _rentals.ListItems(productId);
            return new ApiResponses().OkResult(items.Select(ItemToResponse));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost("Products/{productId:guid}/Items")]
        public async Task<IActionResult> CreateItem(Guid productId, [FromBody] UpsertRentalItemRequest req)
        {
            var product = await _rentals.GetProduct(productId, _tenantContext.TenantId);
            if (product is null) return new ApiResponses().NotFoundResult("Rental product not found.");
            if (product.TrackingKind != "per_item")
            {
                return new ApiResponses().BadRequestResult("This product uses pooled inventory, not per-item tracking.");
            }
            var item = new RentalItem
            {
                TenantId = _tenantContext.TenantId,
                ProductId = productId,
                Label = req.Label.Trim(),
                Serial = string.IsNullOrWhiteSpace(req.Serial) ? null : req.Serial.Trim(),
                Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim(),
                Status = req.Status,
            };
            item.Id = await _rentals.CreateItem(item);
            return new ApiResponses().OkResult(ItemToResponse(item));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPut("Items/{id:guid}")]
        public async Task<IActionResult> UpdateItem(Guid id, [FromBody] UpsertRentalItemRequest req)
        {
            var existing = await _rentals.GetItem(id, _tenantContext.TenantId);
            if (existing is null) return new ApiResponses().NotFoundResult("Rental unit not found.");
            existing.Label = req.Label.Trim();
            existing.Serial = string.IsNullOrWhiteSpace(req.Serial) ? null : req.Serial.Trim();
            existing.Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim();
            existing.Status = req.Status;
            await _rentals.UpdateItem(existing);
            return new ApiResponses().OkResult(ItemToResponse(existing));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpDelete("Items/{id:guid}")]
        public async Task<IActionResult> DeleteItem(Guid id)
        {
            var existing = await _rentals.GetItem(id, _tenantContext.TenantId);
            if (existing is null) return new ApiResponses().NotFoundResult("Rental unit not found.");
            try { await _rentals.DeleteItem(id, _tenantContext.TenantId); }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23503")
            {
                return new ApiResponses().BadRequestResult(
                    "This unit has bookings on file. Set status to retired instead.");
            }
            return new ApiResponses().OkResult();
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private IActionResult? ValidateProduct(UpsertRentalProductRequest req)
        {
            if (req.TrackingKind == "pool" && (req.InventoryPool is null || req.InventoryPool <= 0))
            {
                return new ApiResponses().BadRequestResult("Pooled rentals need a positive inventory.");
            }
            if (req.RiderPaidServiceChargeBps is < 0 or > 10000)
            {
                return new ApiResponses().BadRequestResult("Service-charge share must be 0–100%.");
            }
            return null;
        }

        private async Task<RentalProductResponse> ToResponse(RentalProduct p, bool includeItemCounts)
        {
            var resp = new RentalProductResponse
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                ImageUrl = p.ImageUrl,
                DailyRateCents = p.DailyRateCents,
                DepositCents = p.DepositCents,
                TrackingKind = p.TrackingKind,
                InventoryPool = p.InventoryPool,
                RequiresWaiver = p.RequiresWaiver,
                RiderPaidServiceChargeBps = p.RiderPaidServiceChargeBps,
                IsActive = p.IsActive,
                SortOrder = p.SortOrder,
            };
            if (includeItemCounts && p.TrackingKind == "per_item")
            {
                var items = await _rentals.ListItems(p.Id);
                resp.PerItemTotal = items.Count;
                resp.PerItemAvailable = items.Count(i => i.Status == "available");
            }
            return resp;
        }

        private static RentalItemResponse ItemToResponse(RentalItem i) => new()
        {
            Id = i.Id,
            ProductId = i.ProductId,
            Label = i.Label,
            Serial = i.Serial,
            Notes = i.Notes,
            Status = i.Status,
        };

        // ── Rider purchase ────────────────────────────────────────────────────
        // One PaymentIntent for the whole charge: rental fee + service charge +
        // deposit. On return, deposit is refunded (or partly captured for damage)
        // via the existing Stripe Refund path. Pre-auth would be cleaner but the
        // 7-day Stripe hold expiry would force a re-auth flow for any rental
        // longer than a week — punted to phase 2.
        [Authorize]
        [HttpPost("Buy")]
        public async Task<IActionResult> Buy([FromBody] BuyRentalRequest req, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!_tenantContext.Tenant.RentalsEnabled) return new ApiResponses().BadRequestResult("This tenant doesn't offer rentals.");
            if (!TryGetUserId(out var userId)) return new ApiResponses().BadRequestResult("Invalid token.");

            var user = await _users.GetById(userId);
            if (user is null) return new ApiResponses().BadRequestResult("User not found.");
            var tenant = _tenantContext.Tenant;
            if (tenant.RequireEmergencyContact && string.IsNullOrWhiteSpace(user.EmergencyContactPhone))
            {
                return new ApiResponses().BadRequestResult(
                    "Please add an emergency contact on your profile before booking a rental.");
            }

            var product = await _rentals.GetProduct(req.ProductId, _tenantContext.TenantId);
            if (product is null || !product.IsActive) return new ApiResponses().BadRequestResult("Rental not available.");

            var startDate = req.StartDate.Date;
            var endDate = req.EndDate.Date;
            if (endDate < startDate) return new ApiResponses().BadRequestResult("End date must be on or after start date.");
            if (startDate < DateTime.UtcNow.Date.AddDays(-1))
                return new ApiResponses().BadRequestResult("Start date is in the past.");
            var days = (endDate - startDate).Days + 1;
            if (days > 30) return new ApiResponses().BadRequestResult("Rentals are limited to 30 days.");

            var quantity = Math.Max(1, req.Quantity);

            // Capacity check — note this is racy under simultaneous bookings; we accept that
            // for MVP. Wrap in a transaction with SELECT FOR UPDATE if contention shows up.
            if (product.TrackingKind == "pool")
            {
                var reserved = await _rentals.SumOverlappingPoolReserved(product.Id, startDate, endDate);
                var remaining = (product.InventoryPool ?? 0) - reserved;
                if (quantity > remaining)
                {
                    return new ApiResponses().BadRequestResult(
                        remaining <= 0
                            ? "This rental is fully booked for the selected dates."
                            : $"Only {remaining} unit{(remaining == 1 ? string.Empty : "s")} available — you asked for {quantity}.");
                }
            }
            else // per_item
            {
                var availableUnits = await _rentals.PickAvailablePerItemUnits(product.Id, startDate, endDate, quantity);
                if (availableUnits.Count < quantity)
                {
                    return new ApiResponses().BadRequestResult(
                        $"Only {availableUnits.Count} unit(s) available across those dates.");
                }
            }

            // Waiver gate.
            Guid? signatureId = null;
            if (product.RequiresWaiver)
            {
                var activeWaiver = await _waivers.GetActive(_tenantContext.TenantId);
                if (activeWaiver is not null)
                {
                    var sig = await _waivers.GetSignature(userId, activeWaiver.Id);
                    if (sig is null)
                        return new ApiResponses().BadRequestResult(
                            "Please sign the current waiver before booking this rental.");
                    signatureId = sig.Id;
                }
            }

            // Money. Subtotal = daily rate * days * qty. Service charge per tenant rule.
            // Deposit added on top, charged immediately and refunded on return.
            var subtotalCents = product.DailyRateCents * days * quantity;
            CouponApplication? coupon = null;
            if (!string.IsNullOrWhiteSpace(req.CouponCode))
            {
                var v = await _couponValidator.ValidateAsync(_tenantContext.TenantId, req.CouponCode!,
                    scope: "rental", eventId: null, subtotalCents: subtotalCents, userId: userId);
                if (v.error is not null) return new ApiResponses().BadRequestResult(v.error);
                coupon = v.application;
                subtotalCents -= coupon!.DiscountCents;
            }

            var serviceCharge = (int)((long)subtotalCents * tenant.ServiceChargeBps / 10_000L);
            var riderPortion = (int)((long)serviceCharge * product.RiderPaidServiceChargeBps / 10_000L);
            var rentalAmountCents = subtotalCents + riderPortion;
            var depositCents = product.DepositCents * quantity;
            var grossCents = rentalAmountCents + depositCents;

            var purchase = new RentalPurchase
            {
                TenantId = _tenantContext.TenantId,
                ProductId = product.Id,
                PurchaserUserId = userId,
                PurchaserEmail = user.Email,
                PurchaserName = $"{user.FirstName} {user.LastName}".Trim(),
                WaiverSignatureId = signatureId,
                StartDate = startDate,
                EndDate = endDate,
                Quantity = quantity,
                DailyRateCentsFrozen = product.DailyRateCents,
                DaysCount = days,
                AmountCents = rentalAmountCents,
                ServiceChargeCents = riderPortion,
                DepositCents = depositCents,
                Status = "pending",
                PaymentMethod = "stripe",
            };
            var (id, token) = await _rentals.CreatePurchase(purchase);
            purchase.Id = id;
            purchase.RedemptionToken = token;

            if (coupon is not null)
            {
                await _coupons.RecordRedemption(new CouponRedemption
                {
                    CouponId = coupon.Coupon.Id,
                    TenantId = _tenantContext.TenantId,
                    UserId = userId,
                    SourceKind = "rental",
                    SourceId = purchase.Id,
                    DiscountCents = coupon.DiscountCents,
                });
            }

            // Per-item: persist the assignments now so the same units are guaranteed
            // for the rider on pickup. (Pool products don't need this.)
            if (product.TrackingKind == "per_item")
            {
                var picked = await _rentals.PickAvailablePerItemUnits(product.Id, startDate, endDate, quantity);
                if (picked.Count < quantity)
                    return new ApiResponses().BadRequestResult("Lost the units between check and assignment — please retry.");
                await _rentals.AssignItems(purchase.Id, picked);
            }

            // Gift card pays AFTER discounts but BEFORE Stripe.
            GiftCardApplication? gcApp = null;
            if (!string.IsNullOrWhiteSpace(req.GiftCardCode) && grossCents > 0)
            {
                var gcCheck = await _giftCardValidator.ResolveAsync(_tenantContext.TenantId, req.GiftCardCode!, grossCents);
                if (gcCheck.error is not null) return new ApiResponses().BadRequestResult(gcCheck.error);
                gcApp = gcCheck.application;
                await _giftCards.RecordRedemption(new GiftCardRedemption
                {
                    GiftCardId = gcApp!.Card.Id,
                    TenantId = _tenantContext.TenantId,
                    UserId = userId,
                    SourceKind = "rental",
                    SourceId = purchase.Id,
                    AmountCents = gcApp.AmountToApplyCents,
                });
                await _giftCards.ApplyToBalance(gcApp.Card.Id, gcApp.AmountToApplyCents);
            }
            var stripeChargeCents = grossCents - (gcApp?.AmountToApplyCents ?? 0);

            // Free fast-path: gift card fully covered the booking.
            if (stripeChargeCents == 0)
            {
                await _rentals.UpdateStatus(purchase.Id, "paid");
                return new ApiResponses().OkResult(new BuyRentalResponse
                {
                    PurchaseId = purchase.Id,
                    RedemptionToken = purchase.RedemptionToken,
                    ClientSecret = string.Empty,
                    AmountCents = 0,
                    RentalFeeCents = rentalAmountCents,
                    DepositCents = depositCents,
                    RiderServiceChargeCents = riderPortion,
                    GiftCardAppliedCents = gcApp?.AmountToApplyCents ?? 0,
                });
            }

            var metadata = new Dictionary<string, string>
            {
                ["tenant_id"] = _tenantContext.TenantId.ToString(),
                ["rental_purchase_id"] = purchase.Id.ToString(),
                ["user_id"] = userId.ToString(),
                ["product_id"] = product.Id.ToString(),
                ["sale_kind"] = "rental",
            };
            if (gcApp is not null)
            {
                metadata["gift_card_id"] = gcApp.Card.Id.ToString();
                metadata["gift_card_applied_cents"] = gcApp.AmountToApplyCents.ToString();
            }

            PaymentIntentCreated intent;
            try
            {
                intent = await _payments.CreatePaymentIntentAsync(
                    amountCents: stripeChargeCents,
                    currency: "usd",
                    metadata: metadata,
                    receiptEmail: user.Email,
                    ct: ct);
            }
            catch (InvalidOperationException ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }

            await _rentals.SetRentalPaymentIntentId(purchase.Id, intent.IntentId);

            return new ApiResponses().OkResult(new BuyRentalResponse
            {
                PurchaseId = purchase.Id,
                RedemptionToken = purchase.RedemptionToken,
                ClientSecret = intent.ClientSecret,
                AmountCents = stripeChargeCents,
                RentalFeeCents = rentalAmountCents,
                DepositCents = depositCents,
                RiderServiceChargeCents = riderPortion,
                GiftCardAppliedCents = gcApp?.AmountToApplyCents ?? 0,
            });
        }

        [Authorize]
        [HttpGet("Mine")]
        public async Task<IActionResult> ListMine()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!TryGetUserId(out var userId)) return new ApiResponses().BadRequestResult("Invalid token.");

            var rows = await _rentals.ListMine(userId, _tenantContext.TenantId);
            // Hydrate product names; small N so a per-row lookup is fine for now.
            var products = (await _rentals.ListProducts(_tenantContext.TenantId, activeOnly: false))
                .ToDictionary(p => p.Id, p => p.Name);

            return new ApiResponses().OkResult(rows.Select(r => new MyRentalResponse
            {
                Id = r.Id,
                RedemptionToken = r.RedemptionToken,
                ProductName = products.GetValueOrDefault(r.ProductId, "Rental"),
                StartDate = r.StartDate,
                EndDate = r.EndDate,
                Quantity = r.Quantity,
                AmountCents = r.AmountCents,
                DepositCents = r.DepositCents,
                Status = r.Status,
                CreatedAtUtc = DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc),
            }));
        }

        // ── Counter staff: list / mark out / mark returned ─────────────────────
        [Authorize(Policy = TenantPermissions.Policy.SalesCounter)]
        [HttpGet("Counter")]
        public async Task<IActionResult> CounterList(
            [FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc, [FromQuery] string? status)
        {
            var from = fromUtc ?? DateTime.UtcNow.Date.AddDays(-1);
            var to = toUtc ?? DateTime.UtcNow.Date.AddDays(7);
            var rows = await _rentals.ListForCounter(_tenantContext.TenantId, from, to, status);
            var products = (await _rentals.ListProducts(_tenantContext.TenantId, activeOnly: false))
                .ToDictionary(p => p.Id, p => p.Name);

            // Hydrate per-item labels for any rental whose product is per_item; the
            // counter UI renders one photo-capture row per assigned unit.
            var allItems = new Dictionary<Guid, RentalItem>();
            foreach (var p in (await _rentals.ListProducts(_tenantContext.TenantId, activeOnly: false))
                .Where(p => p.TrackingKind == "per_item"))
            {
                foreach (var unit in await _rentals.ListItems(p.Id))
                {
                    allItems[unit.Id] = unit;
                }
            }

            var responses = new List<object>();
            foreach (var r in rows)
            {
                var assignedItems = r.Status is "paid" or "out" or "returned" or "damaged"
                    ? await _rentals.ListAssignedItems(r.Id) : new();
                responses.Add(new
                {
                    r.Id,
                    r.RedemptionToken,
                    ProductId = r.ProductId,
                    ProductName = products.GetValueOrDefault(r.ProductId, "Rental"),
                    PurchaserName = r.PurchaserName,
                    PurchaserEmail = r.PurchaserEmail,
                    StartDate = r.StartDate,
                    EndDate = r.EndDate,
                    r.Quantity,
                    AmountCents = r.AmountCents,
                    DepositCents = r.DepositCents,
                    DepositCapturedCents = r.DepositCapturedCents,
                    r.Status,
                    CheckedOutAtUtc = r.CheckedOutAt is null ? null : (DateTime?)DateTime.SpecifyKind(r.CheckedOutAt.Value, DateTimeKind.Utc),
                    ReturnedAtUtc = r.ReturnedAt is null ? null : (DateTime?)DateTime.SpecifyKind(r.ReturnedAt.Value, DateTimeKind.Utc),
                    AssignedItems = assignedItems.Select(a => new
                    {
                        PurchaseItemId = a.Id,
                        ItemId = a.ItemId,
                        Label = allItems.GetValueOrDefault(a.ItemId)?.Label,
                        a.CheckoutPhotoDataUrl,
                        a.CheckoutNotes,
                        a.ReturnPhotoDataUrl,
                        a.ReturnNotes,
                    }),
                });
            }
            return new ApiResponses().OkResult(responses);
        }

        [Authorize(Policy = TenantPermissions.Policy.SalesCounter)]
        [HttpPost("Counter/{id:guid}/MarkOut")]
        public async Task<IActionResult> MarkOut(Guid id, [FromBody] MarkOutRequest? req)
        {
            var rental = await _rentals.GetPurchase(id);
            if (rental is null || rental.TenantId != _tenantContext.TenantId)
                return new ApiResponses().NotFoundResult("Rental not found.");
            if (rental.Status != "paid")
                return new ApiResponses().BadRequestResult($"Can't mark out a rental with status '{rental.Status}'.");

            // Per-item photo + notes capture (only meaningful for per_item products).
            if (req?.Items is { Count: > 0 })
            {
                var assigned = (await _rentals.ListAssignedItems(id)).ToDictionary(a => a.Id);
                foreach (var input in req.Items)
                {
                    if (!assigned.ContainsKey(input.PurchaseItemId)) continue;
                    if (!IsValidPhotoOrNull(input.PhotoDataUrl, out var photoErr))
                        return new ApiResponses().BadRequestResult(photoErr!);
                    await _rentals.SetCheckoutCondition(input.PurchaseItemId, input.PhotoDataUrl, input.Notes);
                }
            }

            await _rentals.MarkOut(id, DateTime.UtcNow);
            return new ApiResponses().OkResult(new { id, status = "out" });
        }

        [Authorize(Policy = TenantPermissions.Policy.SalesCounter)]
        [HttpPost("Counter/{id:guid}/MarkReturned")]
        public async Task<IActionResult> MarkReturned(Guid id, [FromBody] MarkReturnedRequest req)
        {
            var rental = await _rentals.GetPurchase(id);
            if (rental is null || rental.TenantId != _tenantContext.TenantId)
                return new ApiResponses().NotFoundResult("Rental not found.");
            if (rental.Status is not "out" and not "paid")
                return new ApiResponses().BadRequestResult($"Can't mark returned a rental with status '{rental.Status}'.");

            var captured = Math.Max(0, Math.Min(rental.DepositCents, req.DepositCapturedCents));
            var damaged = captured > 0;

            // Refund the un-captured portion of the deposit. If captured == deposit, no refund.
            // Issue a partial refund against the rental PI for (DepositCents - captured).
            var refundCents = rental.DepositCents - captured;
            if (refundCents > 0 && !string.IsNullOrEmpty(rental.RentalPiId))
            {
                try
                {
                    await _payments.RefundAsync(rental.RentalPiId!, refundCents,
                        idempotencyKey: $"refund-rental-deposit-{rental.Id}-{refundCents}");
                }
                catch
                {
                    // Surface the refund failure but still flip status — admin can retry the
                    // refund manually from the Stripe dashboard if the API errored.
                    return new ApiResponses().BadRequestResult(
                        "Could not issue deposit refund via Stripe. Mark the rental returned again after fixing.");
                }
            }

            // Per-item return photos / notes.
            if (req.Items is { Count: > 0 })
            {
                var assigned = (await _rentals.ListAssignedItems(id)).ToDictionary(a => a.Id);
                foreach (var input in req.Items)
                {
                    if (!assigned.ContainsKey(input.PurchaseItemId)) continue;
                    if (!IsValidPhotoOrNull(input.PhotoDataUrl, out var photoErr))
                        return new ApiResponses().BadRequestResult(photoErr!);
                    await _rentals.SetReturnCondition(input.PurchaseItemId, input.PhotoDataUrl, input.Notes);
                }
            }

            await _rentals.MarkReturned(id, DateTime.UtcNow, req.ConditionNotes, captured, damaged);
            return new ApiResponses().OkResult(new { id, status = damaged ? "damaged" : "returned" });
        }

        private static bool IsValidPhotoOrNull(string? dataUrl, out string? error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(dataUrl)) return true;   // photo is optional
            var ok = (dataUrl.StartsWith("data:image/jpeg;base64,", StringComparison.Ordinal)
                     || dataUrl.StartsWith("data:image/png;base64,", StringComparison.Ordinal))
                     && dataUrl.Length is > 200 and < 2_000_000;
            if (!ok) error = "Photo must be a JPEG/PNG data-url under ~2MB.";
            return ok;
        }

        // ── Maintenance windows (admin) ───────────────────────────────────────
        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpGet("Items/{itemId:guid}/Maintenance")]
        public async Task<IActionResult> ListMaintenance(Guid itemId)
        {
            var item = await _rentals.GetItem(itemId, _tenantContext.TenantId);
            if (item is null) return new ApiResponses().NotFoundResult("Rental unit not found.");
            var rows = await _rentals.ListMaintenanceForItem(itemId);
            return new ApiResponses().OkResult(rows.Select(MaintenanceToResponse));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost("Items/{itemId:guid}/Maintenance")]
        public async Task<IActionResult> AddMaintenance(Guid itemId, [FromBody] UpsertMaintenanceRequest req)
        {
            var item = await _rentals.GetItem(itemId, _tenantContext.TenantId);
            if (item is null) return new ApiResponses().NotFoundResult("Rental unit not found.");
            if (req.EndsAtDate.Date < req.StartsAtDate.Date)
                return new ApiResponses().BadRequestResult("End date must be on or after start date.");
            var m = new RentalItemMaintenance
            {
                TenantId = _tenantContext.TenantId,
                ItemId = itemId,
                StartsAtDate = req.StartsAtDate.Date,
                EndsAtDate = req.EndsAtDate.Date,
                Reason = string.IsNullOrWhiteSpace(req.Reason) ? null : req.Reason.Trim(),
            };
            m.Id = await _rentals.AddMaintenance(m);
            return new ApiResponses().OkResult(MaintenanceToResponse(m));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPut("Maintenance/{id:guid}")]
        public async Task<IActionResult> UpdateMaintenance(Guid id, [FromBody] UpsertMaintenanceRequest req)
        {
            var existing = await _rentals.GetMaintenance(id, _tenantContext.TenantId);
            if (existing is null) return new ApiResponses().NotFoundResult("Maintenance window not found.");
            if (req.EndsAtDate.Date < req.StartsAtDate.Date)
                return new ApiResponses().BadRequestResult("End date must be on or after start date.");
            existing.StartsAtDate = req.StartsAtDate.Date;
            existing.EndsAtDate = req.EndsAtDate.Date;
            existing.Reason = string.IsNullOrWhiteSpace(req.Reason) ? null : req.Reason.Trim();
            await _rentals.UpdateMaintenance(existing);
            return new ApiResponses().OkResult(MaintenanceToResponse(existing));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpDelete("Maintenance/{id:guid}")]
        public async Task<IActionResult> DeleteMaintenance(Guid id)
        {
            var existing = await _rentals.GetMaintenance(id, _tenantContext.TenantId);
            if (existing is null) return new ApiResponses().NotFoundResult("Maintenance window not found.");
            await _rentals.DeleteMaintenance(id, _tenantContext.TenantId);
            return new ApiResponses().OkResult();
        }

        private static MaintenanceResponse MaintenanceToResponse(RentalItemMaintenance m) => new()
        {
            Id = m.Id,
            ItemId = m.ItemId,
            StartsAtDate = m.StartsAtDate,
            EndsAtDate = m.EndsAtDate,
            Reason = m.Reason,
        };

        private bool TryGetUserId(out Guid userId)
        {
            var claim = User.FindFirst("UserId")?.Value;
            return Guid.TryParse(claim, out userId);
        }
    }
}
