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
using webapi.Multitenancy;

namespace webapi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SeasonPassController : ControllerBase
    {
        private readonly ISeasonPassRepository _passes;
        private readonly IEventRepository _events;
        private readonly IUserRepository _users;
        private readonly IPaymentProvider _payments;
        private readonly ICouponRepository _coupons;
        private readonly ICouponValidator _couponValidator;
        private readonly IGiftCardRepository _giftCards;
        private readonly Services.GiftCards.IGiftCardValidator _giftCardValidator;
        private readonly IMembershipRepository _memberships;
        private readonly IWaiverRepository _waivers;
        private readonly Services.Waivers.IWaiverCheckInGate _waiverGate;
        private readonly ITenantContext _tenantContext;

        public SeasonPassController(
            ISeasonPassRepository passes,
            IEventRepository events,
            IUserRepository users,
            IPaymentProvider payments,
            ICouponRepository coupons,
            ICouponValidator couponValidator,
            IGiftCardRepository giftCards,
            Services.GiftCards.IGiftCardValidator giftCardValidator,
            IMembershipRepository memberships,
            IWaiverRepository waivers,
            Services.Waivers.IWaiverCheckInGate waiverGate,
            ITenantContext tenantContext)
        {
            _passes = passes;
            _events = events;
            _users = users;
            _payments = payments;
            _coupons = coupons;
            _couponValidator = couponValidator;
            _giftCards = giftCards;
            _giftCardValidator = giftCardValidator;
            _memberships = memberships;
            _waivers = waivers;
            _waiverGate = waiverGate;
            _tenantContext = tenantContext;
        }

        // ── Products: public list ─────────────────────────────────────────────────
        [HttpGet("Products")]
        public async Task<IActionResult> ListActive()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var products = await _passes.ListProductsForTenant(_tenantContext.TenantId, activeOnly: true);
            var responses = new List<SeasonPassProductResponse>();
            foreach (var p in products) responses.Add(await ToResponse(p));
            return new ApiResponses().OkResult(responses);
        }

        // ── Products: tenant-admin CRUD ───────────────────────────────────────────
        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpGet("Products/Admin")]
        public async Task<IActionResult> ListForAdmin()
        {
            var products = await _passes.ListProductsForTenant(_tenantContext.TenantId, activeOnly: false);
            var responses = new List<SeasonPassProductResponse>();
            foreach (var p in products) responses.Add(await ToResponse(p));
            return new ApiResponses().OkResult(responses);
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
            await _passes.ReplacePerks(product.Id, request.Perks.Select(p => new SeasonPassEventTypePerk
            {
                EventTypeId = p.EventTypeId, DiscountPercent = p.DiscountPercent,
            }));
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
            await _passes.ReplacePerks(existing.Id, request.Perks.Select(p => new SeasonPassEventTypePerk
            {
                EventTypeId = p.EventTypeId, DiscountPercent = p.DiscountPercent,
            }));
            return new ApiResponses().OkResult(await ToResponse(existing));
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

            var product = await _passes.GetProduct(request.ProductId, _tenantContext.TenantId);
            if (product is null || !product.IsActive) return new ApiResponses().BadRequestResult("Pass is not available.");

            if (!IsValidPhotoDataUrl(request.PhotoDataUrl))
            {
                return new ApiResponses().BadRequestResult("A photo of the pass holder is required for ID verification at the gate.");
            }

            // Waiver gate at purchase: when the pass requires a waiver, the holder signs the
            // current waiver now so they don't have to at the gate. We record the signature on
            // the purchase. If the tenant later publishes a new version, the check-in gate
            // re-prompts and the holder re-signs through the normal waiver flow.
            Guid? waiverSignatureId = null;
            if (product.RequiresWaiver)
            {
                var activeWaiver = await _waivers.GetActive(_tenantContext.TenantId);
                if (activeWaiver is not null)
                {
                    var sig = await _waivers.GetSignature(userId, activeWaiver.Id);
                    if (sig is null)
                    {
                        return new ApiResponses().BadRequestResult(
                            "This season pass requires a signed waiver. Please sign the current waiver before purchasing.");
                    }
                    waiverSignatureId = sig.Id;
                }
            }

            var tenant = _tenantContext.Tenant;
            var basePrice = product.PriceCents;

            // Coupon: applies pre-service-charge.
            CouponApplication? spCoupon = null;
            if (!string.IsNullOrWhiteSpace(request.CouponCode))
            {
                var v = await _couponValidator.ValidateAsync(_tenantContext.TenantId, request.CouponCode!,
                    scope: "season_pass", eventId: null, subtotalCents: basePrice, userId: userId);
                if (v.error is not null) return new ApiResponses().BadRequestResult(v.error);
                spCoupon = v.application;
                basePrice -= spCoupon!.DiscountCents;
            }

            var serviceCharge = (int)((long)basePrice * tenant.ServiceChargeBps / 10_000L);
            var riderPortion = (int)((long)serviceCharge * product.RiderPaidServiceChargeBps / 10_000L);
            var amountCents = basePrice + riderPortion;

            var purchase = new SeasonPassPurchase
            {
                TenantId = _tenantContext.TenantId,
                PurchaserUserId = userId,
                ProductId = product.Id,
                AmountCents = amountCents,
                ServiceChargeCents = serviceCharge,
                PaymentMethod = "stripe",
                Status = "pending",
                PurchaserEmail = user.Email,
                PurchaserName = $"{user.FirstName} {user.LastName}".Trim(),
                ValidFromDate = product.ValidFromDate,
                ValidToDate = product.ValidToDate,
                CreditsRemaining = product.Kind == "credits" ? product.TotalCredits : null,
                PhotoDataUrl = request.PhotoDataUrl,
                WaiverSignatureId = waiverSignatureId,
            };
            var (id, token) = await _passes.CreatePurchase(purchase);
            purchase.Id = id;
            purchase.RedemptionToken = token;

            if (spCoupon is not null)
            {
                await _coupons.RecordRedemption(new CouponRedemption
                {
                    CouponId = spCoupon.Coupon.Id,
                    TenantId = _tenantContext.TenantId,
                    UserId = userId,
                    SourceKind = "season_pass",
                    SourceId = purchase.Id,
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
                await _giftCards.RecordRedemption(new GiftCardRedemption
                {
                    GiftCardId = spGift!.Card.Id,
                    TenantId = _tenantContext.TenantId,
                    UserId = userId,
                    SourceKind = "season_pass",
                    SourceId = purchase.Id,
                    AmountCents = spGift.AmountToApplyCents,
                });
                await _giftCards.ApplyToBalance(spGift.Card.Id, spGift.AmountToApplyCents);
            }
            var spStripeChargeCents = amountCents - (spGift?.AmountToApplyCents ?? 0);

            // Free fast-path: gift card fully covered the pass.
            if (spStripeChargeCents == 0)
            {
                await _passes.UpdatePurchaseStatus(purchase.Id, "paid");
                return new ApiResponses().OkResult(new BuySeasonPassResponse
                {
                    PurchaseId = id,
                    RedemptionToken = token,
                    ClientSecret = string.Empty,
                    AmountCents = 0,
                    RiderServiceChargeCents = 0,
                    GiftCardAppliedCents = spGift?.AmountToApplyCents ?? 0,
                });
            }

            var metadata = new Dictionary<string, string>
            {
                ["tenant_id"] = _tenantContext.TenantId.ToString(),
                ["season_pass_id"] = id.ToString(),
                ["user_id"] = userId.ToString(),
                ["product_id"] = product.Id.ToString(),
                ["sale_kind"] = "season_pass",
            };
            if (spGift is not null)
            {
                metadata["gift_card_applied_cents"] = spGift.AmountToApplyCents.ToString();
                metadata["gift_card_id"] = spGift.Card.Id.ToString();
            }

            PaymentIntentCreated intent;
            try
            {
                intent = await _payments.CreatePaymentIntentAsync(
                    spStripeChargeCents, "usd", metadata, user.Email, ct);
            }
            catch (InvalidOperationException ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }

            await _passes.SetPurchaseStripePaymentIntentId(id, intent.IntentId);

            return new ApiResponses().OkResult(new BuySeasonPassResponse
            {
                PurchaseId = id,
                RedemptionToken = token,
                ClientSecret = intent.ClientSecret,
                AmountCents = spStripeChargeCents,
                RiderServiceChargeCents = riderPortion,
                GiftCardAppliedCents = spGift?.AmountToApplyCents ?? 0,
            });
        }

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

            // A required event waiver can't be skipped at the season-pass gate either. The holder
            // is a rider, so enforce the event's rider waiver.
            var ctx = await _passes.GetReservationForCheckIn(id, _tenantContext.TenantId);
            if (ctx is not null)
            {
                var waiverBlock = await _waiverGate.BlockReason(_tenantContext.TenantId, ctx.EventId,
                    riderAudience: true, ctx.HolderUserId, ctx.HolderEmail, ctx.HolderName);
                if (waiverBlock is not null) return new ApiResponses().BadRequestResult(waiverBlock);
            }

            await _passes.UpdateReservationStatus(id, _tenantContext.TenantId, "checked_in", staffId);
            return new ApiResponses().OkResult();
        }

        private bool TryGetUserId(out Guid userId)
        {
            var claim = User.FindFirst("UserId")?.Value;
            return Guid.TryParse(claim, out userId);
        }

        private async Task<SeasonPassProductResponse> ToResponse(SeasonPassProduct p)
        {
            var perks = await _passes.ListPerks(p.Id);
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
                Perks = perks.Select(x => new EventTypePerkInput { EventTypeId = x.EventTypeId, DiscountPercent = x.DiscountPercent }).ToList(),
            };
        }
    }
}
