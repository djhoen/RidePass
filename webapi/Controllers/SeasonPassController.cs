using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Coupons;
using Services.Helpers;
using Services.Payments;
using Services.Repositories.Data.CouponData;
using Services.Repositories.Data.GiftCardData;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Data.TenantData;
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
        private readonly IEventTicketTierRepository _tiers;
        private readonly IEventTicketPurchaseRepository _ticketPurchases;
        private readonly IPaymentProvider _payments;
        private readonly IChargeRouter _chargeRouter;
        private readonly ICouponRepository _coupons;
        private readonly ICouponValidator _couponValidator;
        private readonly IGiftCardRepository _giftCards;
        private readonly Services.GiftCards.IGiftCardValidator _giftCardValidator;
        private readonly IMembershipRepository _memberships;
        private readonly IWaiverRepository _waivers;
        private readonly Services.Waivers.IWaiverCheckInGate _waiverGate;
        private readonly Services.Riders.IRiderIdVerification _idVerification;
        private readonly ITenantLedgerRepository _ledger;
        private readonly Services.Payments.IFeeCalculator _feeCalculator;
        private readonly ITenantContext _tenantContext;
        private readonly Services.Helpers.Interfaces.IDbHelper _db;
        private readonly Services.Audit.IAuditLogger _audit;
        private readonly Services.Storage.IImageStorage _imageStorage;

        public SeasonPassController(
            ISeasonPassRepository passes,
            IEventRepository events,
            ITenantEventTypeRepository eventTypes,
            IUserRepository users,
            IEventTicketTierRepository tiers,
            IEventTicketPurchaseRepository ticketPurchases,
            IPaymentProvider payments,
            IChargeRouter chargeRouter,
            ICouponRepository coupons,
            ICouponValidator couponValidator,
            IGiftCardRepository giftCards,
            Services.GiftCards.IGiftCardValidator giftCardValidator,
            IMembershipRepository memberships,
            IWaiverRepository waivers,
            Services.Waivers.IWaiverCheckInGate waiverGate,
            Services.Riders.IRiderIdVerification idVerification,
            ITenantLedgerRepository ledger,
            Services.Payments.IFeeCalculator feeCalculator,
            ITenantContext tenantContext,
            Services.Helpers.Interfaces.IDbHelper db,
            Services.Audit.IAuditLogger audit,
            Services.Storage.IImageStorage imageStorage)
        {
            _passes = passes;
            _events = events;
            _eventTypes = eventTypes;
            _users = users;
            _tiers = tiers;
            _ticketPurchases = ticketPurchases;
            _payments = payments;
            _chargeRouter = chargeRouter;
            _coupons = coupons;
            _couponValidator = couponValidator;
            _giftCards = giftCards;
            _giftCardValidator = giftCardValidator;
            _memberships = memberships;
            _waivers = waivers;
            _waiverGate = waiverGate;
            _idVerification = idVerification;
            _ledger = ledger;
            _feeCalculator = feeCalculator;
            _tenantContext = tenantContext;
            _db = db;
            _audit = audit;
            _imageStorage = imageStorage;
        }

        // ── Products: public list ─────────────────────────────────────────────────
        [HttpGet("Products")]
        public async Task<IActionResult> ListActive()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var products = await _passes.ListProductsForTenant(_tenantContext.TenantId, activeOnly: true);
            return new ApiResponses().OkResult(await ToResponses(products));
        }

        /// <summary>
        /// One pass product's landing page (marketing content + live product facts).
        /// Public; accepts a slug or a product id (the embed widget links by id).
        /// Drafts and inactive products 404 for the public but stay visible to staff
        /// holding CatalogManage, so the admin "preview" link works before publishing.
        /// </summary>
        [HttpGet("Landing/{slugOrId}")]
        public async Task<IActionResult> GetLanding(string slugOrId)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var byId = Guid.TryParse(slugOrId, out var id);
            var product = byId
                ? await _passes.GetProduct(id, _tenantContext.TenantId)
                : await _passes.GetProductBySlug(slugOrId, _tenantContext.TenantId);
            if (product is null)
            {
                return new ApiResponses().NotFoundResult("This pass isn't available.");
            }

            var isStaff = CallerHasCatalogManage();
            if (!product.IsActive && !isStaff)
            {
                return new ApiResponses().NotFoundResult("This pass isn't available.");
            }
            // Employee products are staff grants, not merchandise. Same 404 as not-found so a
            // guessed slug or id cannot even confirm one exists.
            if (product.IsEmployee && !isStaff)
            {
                return new ApiResponses().NotFoundResult("This pass isn't available.");
            }
            // Slug URLs are the marketing surface: a draft slug 404s (same message as
            // not-found so its existence isn't leaked). Id lookups are the embed widget,
            // which must keep working for passes with no landing at all — the product is
            // already public via the products list — so an id resolves regardless, with
            // unpublished landing CONTENT stripped below for non-staff.
            var landingVisible = product.LandingPublished || isStaff;
            if (!byId && !landingVisible)
            {
                return new ApiResponses().NotFoundResult("This pass isn't available.");
            }

            var projected = await ToResponse(product);
            return new ApiResponses().OkResult(new SeasonPassLandingResponse
            {
                Id = product.Id,
                Name = product.Name,
                Description = product.Description,
                PriceCents = product.PriceCents,
                ValidFromDate = product.ValidFromDate,
                ValidToDate = product.ValidToDate,
                Kind = product.Kind,
                ValidDaysOfWeek = product.ValidDaysOfWeek,
                TotalCredits = product.TotalCredits,
                RequiresWaiver = product.RequiresWaiver,
                RiderPaidServiceChargeBps = product.RiderPaidServiceChargeBps,
                Slug = product.Slug,
                HeroImageUrl = landingVisible ? product.HeroImageUrl : null,
                LandingHtml = landingVisible ? product.LandingHtml : null,
                LandingPublished = product.LandingPublished,
                Benefits = projected.Benefits,
            });
        }

        // ── Products: tenant-admin CRUD ───────────────────────────────────────────
        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpGet("Products/Admin")]
        public async Task<IActionResult> ListForAdmin()
        {
            // The only caller that includes employee products: this is where they are configured.
            var products = await _passes.ListProductsForTenant(
                _tenantContext.TenantId, activeOnly: false, includeEmployee: true);
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
            // A free product is only legal for an employee grant. Mirrors the database's
            // chk_season_pass_product_price so the API refuses what the DB would refuse anyway,
            // with a message that explains it instead of surfacing a constraint violation.
            if (request.PriceCents < 1 && !request.IsEmployee)
            {
                return new ApiResponses().BadRequestResult(
                    "A pass has to cost something. Only an employee pass can be free.");
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
                IsEmployee = request.IsEmployee,
                SortOrder = request.SortOrder,
                HeroImageUrl = Trim(request.HeroImageUrl),
                LandingHtml = Trim(request.LandingHtml),
                LandingPublished = request.LandingPublished,
            };
            product.Slug = await ResolveLandingSlug(request, excludeId: null);
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
            // A free product is only legal for an employee grant. Mirrors the database's
            // chk_season_pass_product_price so the API refuses what the DB would refuse anyway,
            // with a message that explains it instead of surfacing a constraint violation.
            if (request.PriceCents < 1 && !request.IsEmployee)
            {
                return new ApiResponses().BadRequestResult(
                    "A pass has to cost something. Only an employee pass can be free.");
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
            existing.IsEmployee = request.IsEmployee;
            existing.SortOrder = request.SortOrder;
            existing.HeroImageUrl = Trim(request.HeroImageUrl);
            existing.LandingHtml = Trim(request.LandingHtml);
            existing.LandingPublished = request.LandingPublished;
            existing.Slug = await ResolveLandingSlug(request, excludeId: id);
            await _passes.UpdateProduct(existing);
            var updateError = await SaveBenefits(existing.Id, request);
            if (updateError is not null) return new ApiResponses().BadRequestResult(updateError);
            return new ApiResponses().OkResult(await ToResponse(existing));
        }

        // Landing hero / inline-body image upload, decoupled from row mutation (same pattern
        // as PageController/BlogController): returns a URL the editor patches onto the product
        // on save.
        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost("Products/Image")]
        [RequestSizeLimit(5 * 1024 * 1024)]
        public async Task<IActionResult> UploadLandingImage(IFormFile file, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var (ext, error) = ValidateImage(file);
            if (error is not null) return new ApiResponses().BadRequestResult(error);
            await using var stream = file.OpenReadStream();
            var url = await _imageStorage.SaveAsync(stream, _tenantContext.TenantId, "passes", ext!, ct);
            return new ApiResponses().OkResult(new { imageUrl = url });
        }

        private static (string? ext, string? error) ValidateImage(IFormFile file)
        {
            if (file is null || file.Length == 0) return (null, "File is required.");
            if (file.Length > 5 * 1024 * 1024) return (null, "File exceeds 5 MB limit.");
            var allowed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["image/png"] = ".png",
                ["image/jpeg"] = ".jpg",
                ["image/webp"] = ".webp",
            };
            if (!allowed.TryGetValue(file.ContentType, out var ext))
                return (null, $"Unsupported content type: {file.ContentType}.");
            return (ext, null);
        }

        /// <summary>
        /// The product's landing slug: null when the product has no landing content at all,
        /// otherwise the requested slug (or one derived from the name), de-duplicated per
        /// tenant with a -2/-3 suffix (Pages precedent). No reserved-word list is needed —
        /// landing URLs live under their own /SeasonPasses/* namespace.
        /// </summary>
        private async Task<string?> ResolveLandingSlug(UpsertSeasonPassProductRequest request, Guid? excludeId)
        {
            var hasLanding = request.LandingPublished
                || !string.IsNullOrWhiteSpace(request.Slug)
                || !string.IsNullOrWhiteSpace(request.LandingHtml)
                || !string.IsNullOrWhiteSpace(request.HeroImageUrl);
            if (!hasLanding) return null;

            var baseSlug = Slugify(string.IsNullOrWhiteSpace(request.Slug) ? request.Name : request.Slug!);
            var slug = baseSlug;
            var n = 2;
            while (await _passes.ProductSlugExists(slug, _tenantContext.TenantId, excludeId))
            {
                slug = $"{baseSlug}-{n++}";
            }
            return slug;
        }

        private static string Slugify(string input)
        {
            var lower = (input ?? "").Trim().ToLowerInvariant();
            var sb = new System.Text.StringBuilder(lower.Length);
            var lastHyphen = false;
            foreach (var ch in lower)
            {
                if (char.IsLetterOrDigit(ch))
                {
                    sb.Append(ch);
                    lastHyphen = false;
                }
                else if (!lastHyphen && sb.Length > 0)
                {
                    sb.Append('-');
                    lastHyphen = true;
                }
            }
            var slug = sb.ToString().Trim('-');
            return slug.Length == 0 ? "pass" : slug;
        }

        private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        // Imperative CatalogManage check for the public landing endpoint's draft-preview
        // carve-out (an [Authorize] attribute would lock the whole endpoint).
        private bool CallerHasCatalogManage()
        {
            var roles = User.FindAll("role").Select(c => c.Value);
            return TenantPermissions.ForRoles(roles).Contains(TenantPermissions.CatalogManage);
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
                if (b.BenefitType == "buddy_pass")
                {
                    // Zero scopes = a perk that admits nobody. Rejected rather than defaulted to
                    // "everywhere": the permissive reading silently hands out free race entries.
                    if (request.BuddyEventTypeIds.Count == 0 && !request.BuddyIncludeWalkUp)
                    {
                        return "Choose at least one event type (or walk-up days) the buddy passes are good for.";
                    }
                    if (request.BuddyEventTypeIds.Any(id => !eventTypeIds.Contains(id)))
                    {
                        return "One of the buddy pass event types doesn't exist at this track.";
                    }
                    // With no event there is no tier and no price, so a percentage has nothing to
                    // take a percentage OF. Free is the only coherent walk-up buddy pass.
                    var buddyFree = b.DiscountKind == "percent" && b.DiscountValue >= 10_000;
                    if (request.BuddyIncludeWalkUp && !buddyFree)
                    {
                        return "A buddy pass valid on days with no event has to cover admission outright — "
                             + "there's no ticket price to discount.";
                    }
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

            // Scopes hang off the buddy benefit row, so they can only be written after
            // ReplaceBenefits has created it (the old row and its id are gone by then).
            if (benefits.Any(b => b.BenefitType == "buddy_pass"))
            {
                var saved = await _passes.ListBenefits(productId, _tenantContext.TenantId);
                var buddyBenefit = saved.FirstOrDefault(b => b.BenefitType == "buddy_pass");
                if (buddyBenefit is not null)
                {
                    await _passes.ReplaceBuddyScopes(buddyBenefit.Id, _tenantContext.TenantId,
                        request.BuddyEventTypeIds, request.BuddyIncludeWalkUp);
                }
            }

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

        // ── Upgrades (Script0253) ───────────────────────────────────────────────

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpGet("Upgrades")]
        public async Task<IActionResult> ListUpgrades()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var paths = await _passes.ListUpgradePaths(_tenantContext.TenantId);
            var counts = await _passes.CountEligibleHolders(_tenantContext.TenantId, TenantToday());
            var products = (await _passes.ListProductsForTenant(
                    _tenantContext.TenantId, activeOnly: false, includeEmployee: false))
                .ToList();

            return new ApiResponses().OkResult(new UpgradePathsResponse
            {
                Paths = paths.Select(x => new UpgradePathItem
                {
                    Id = x.Id,
                    FromProductId = x.FromProductId,
                    ToProductId = x.ToProductId,
                    FromProductName = x.FromProductName,
                    ToProductName = x.ToProductName,
                    PriceCents = x.PriceCents,
                    IsActive = x.IsActive,
                    EligibleHolders = counts.GetValueOrDefault(x.Id, 0),
                }).ToList(),
                // Employee products are excluded by the repository default, which is also what
                // keeps them off both axes of the matrix.
                Products = products.Select(x => new UpgradeProductOption
                {
                    Id = x.Id,
                    Name = x.Name,
                    Kind = x.Kind,
                    PriceCents = x.PriceCents,
                    IsActive = x.IsActive,
                }).ToList(),
            });
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPut("Upgrades")]
        public async Task<IActionResult> UpsertUpgrade([FromBody] UpsertUpgradePathRequest request)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (request.FromProductId == request.ToProductId)
            {
                return new ApiResponses().BadRequestResult("A pass can't upgrade to itself.");
            }
            await _passes.UpsertUpgradePath(_tenantContext.TenantId,
                request.FromProductId, request.ToProductId, request.PriceCents, request.IsActive);
            return new ApiResponses().OkResult();
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpDelete("Upgrades/{id:guid}")]
        public async Task<IActionResult> DeleteUpgrade(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            await _passes.DeleteUpgradePath(id, _tenantContext.TenantId);
            return new ApiResponses().OkResult();
        }

        /// <summary>Upgrades the signed-in rider can take right now, one per (pass, offer).</summary>
        [Authorize]
        [HttpGet("MyUpgrades")]
        public async Task<IActionResult> MyUpgrades()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!TryGetUserId(out var userId)) return new ApiResponses().BadRequestResult("Invalid token.");
            var offers = await _passes.ListUpgradeOffersForUser(userId, _tenantContext.TenantId, TenantToday());
            return new ApiResponses().OkResult(offers.Select(ToUpgradeOfferItem).ToList());
        }

        private static UpgradeOfferItem ToUpgradeOfferItem(SeasonPassUpgradeOffer o) => new()
        {
            PathId = o.PathId,
            PassPurchaseId = o.PassPurchaseId,
            FromProductName = o.FromProductName,
            ToProductId = o.ToProductId,
            ToProductName = o.ToProductName,
            ToProductDescription = o.ToProductDescription,
            ToProductKind = o.ToProductKind,
            ToProductTotalCredits = o.ToProductTotalCredits,
            ToValidFromDate = o.ToValidFromDate,
            ToValidToDate = o.ToValidToDate,
            PriceCents = o.PriceCents,
        };

        /// <summary>
        /// Take an upgrade. Creates the replacement pass and charges the path price; the OLD pass
        /// is not touched until the money lands (StripePurchaseFinalizer), because until then the
        /// rider still owns it and retiring it early would strand them with nothing.
        /// </summary>
        [Authorize]
        [HttpPost("Upgrade")]
        public async Task<IActionResult> BuyUpgrade([FromBody] BuyUpgradeRequest request, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!_tenantContext.Tenant.SeasonPassesEnabled)
            {
                return new ApiResponses().BadRequestResult("Season passes aren't sold at this track.");
            }
            if (!TryGetUserId(out var userId)) return new ApiResponses().BadRequestResult("Invalid token.");
            var user = await _users.GetById(userId);
            if (user is null) return new ApiResponses().BadRequestResult("User not found.");

            // Serialize concurrent upgrades of the same pass. Without it a double-submit gets two
            // replacements past the eligibility read (which only excludes replacements that already
            // exist), and on the free path both settle immediately, so the rider ends up holding a
            // pass they were never issued. Same per-pass lock contract the redeem paths use.
            await using var upgradeLock = await _db.AcquireAdvisoryLock($"season-pass-upgrade:{request.PassPurchaseId}");

            // Re-resolved server-side, under the lock: the client sends only ids, so a stale price
            // it was shown, or a hand-edited one, can never be what gets charged.
            var offer = await _passes.GetUpgradeOffer(
                request.PassPurchaseId, request.PathId, userId, _tenantContext.TenantId, TenantToday());
            if (offer is null)
            {
                return new ApiResponses().BadRequestResult(
                    "That upgrade isn't available any more. Your pass may have been used up, expired, or already upgraded.");
            }

            var replacement = new SeasonPassPurchase
            {
                TenantId = _tenantContext.TenantId,
                PurchaserUserId = userId,
                ProductId = offer.ToProductId,
                UpgradedFromPurchaseId = offer.PassPurchaseId,
                AmountCents = offer.PriceCents,
                ServiceChargeCents = 0,
                // A free upgrade never touches Stripe, so it is 'voucher' and immediately paid,
                // matching how every other zero-value issue in the codebase is recorded.
                PaymentMethod = offer.PriceCents <= 0 ? "voucher" : "stripe",
                Status = offer.PriceCents <= 0 ? "paid" : "pending",
                PurchaserEmail = user.Email,
                PurchaserName = $"{user.FirstName} {user.LastName}".Trim(),
                ValidFromDate = offer.ToValidFromDate,
                ValidToDate = offer.ToValidToDate,
                // Credits deliberately do NOT carry from the old pass: upgrading a part-used pack
                // is buying a different thing, not topping one up.
                CreditsRemaining = offer.ToProductTotalCredits,
            };
            var (newId, token) = await _passes.CreatePurchase(replacement);

            if (offer.PriceCents <= 0)
            {
                // Nothing to charge, so do here what the finalizer would have done on payment.
                if (await _passes.MarkUpgraded(offer.PassPurchaseId, _tenantContext.TenantId))
                {
                    await _passes.CarryRegistrationForward(offer.PassPurchaseId, newId, _tenantContext.TenantId);
                }
                await _audit.Log(
                    "season_pass.upgraded",
                    $"Upgraded {offer.FromProductName} to {offer.ToProductName} (free)",
                    targetKind: "season_pass_purchase", targetId: newId,
                    tenantId: _tenantContext.TenantId,
                    metadata: new { from = offer.PassPurchaseId, pathId = offer.PathId });

                return new ApiResponses().OkResult(new
                {
                    passPurchaseId = newId,
                    redemptionToken = token,
                    amountCents = 0,
                    clientSecret = (string?)null,
                });
            }

            var metadata = new Dictionary<string, string>
            {
                ["tenant_id"] = _tenantContext.TenantId.ToString(),
                ["sale_kind"] = "season_pass",
                ["season_pass_upgrade_from"] = offer.PassPurchaseId.ToString(),
            };
            PaymentIntentCreated intent;
            ChargePlan chargePlan;
            try
            {
                chargePlan = _chargeRouter.Plan(_tenantContext.Tenant, 0, offer.PriceCents);
                intent = await _payments.CreatePaymentIntentAsync(
                    offer.PriceCents, "usd", metadata, user.Email,
                    connectedAccountId: chargePlan.ConnectedAccountId,
                    applicationFeeCents: chargePlan.ApplicationFeeCents, ct: ct);
            }
            catch (InvalidOperationException ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }

            await _passes.SetPurchaseStripePaymentIntentId(newId, intent.IntentId);
            if (chargePlan.IsDirect)
            {
                await _passes.MarkPurchaseDirectCharge(newId, _tenantContext.TenantId, chargePlan.ConnectedAccountId!);
            }

            return new ApiResponses().OkResult(new
            {
                passPurchaseId = newId,
                redemptionToken = token,
                amountCents = offer.PriceCents,
                clientSecret = intent.ClientSecret,
            });
        }

        /// <summary>The tenant's local date, which is what "not used up" is measured against.</summary>
        private DateTime TenantToday()
        {
            var tz = _tenantContext.Tenant?.Timezone;
            if (string.IsNullOrWhiteSpace(tz)) return DateTime.UtcNow.Date;
            try
            {
                return TimeZoneInfo.ConvertTimeFromUtc(
                    DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(tz)).Date;
            }
            catch (TimeZoneNotFoundException)
            {
                return DateTime.UtcNow.Date;
            }
        }

        // ── Buddy passes (Script0247) ────────────────────────────────────────────
        // The holder must be present and staff perform the redemption, which is why there is no
        // rider-facing endpoint here and no transferable code anywhere: the holder's pass is
        // scanned to produce PassPurchaseId, and that scan IS the presence proof.

        [Authorize(Policy = TenantPermissions.Policy.SalesCounter)]
        [HttpPost("Buddy/Redeem")]
        public async Task<IActionResult> RedeemBuddyPass([FromBody] RedeemBuddyPassRequest request)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!TryGetUserId(out var staffId)) return new ApiResponses().BadRequestResult("Invalid token.");

            var pass = await _passes.GetPurchase(request.PassPurchaseId);
            if (pass is null || pass.TenantId != _tenantContext.TenantId)
            {
                return new ApiResponses().NotFoundResult("That pass isn't from this track.");
            }
            if (pass.Status != "paid")
            {
                return new ApiResponses().BadRequestResult("That pass isn't active, so it grants nothing.");
            }
            var today = DateTime.UtcNow.Date;
            if (today < pass.ValidFromDate.Date || today > pass.ValidToDate.Date)
            {
                return new ApiResponses().BadRequestResult("That pass isn't valid today.");
            }

            var ent = await _passes.GetBuddyEntitlement(request.PassPurchaseId, _tenantContext.TenantId);
            if (ent is null)
            {
                return new ApiResponses().BadRequestResult("This pass doesn't include buddy passes.");
            }
            if (ent.Scopes.Count == 0)
            {
                // Configured with a quantity but nothing to spend it on. Say so plainly rather
                // than "no buddy passes left", which would send staff hunting the wrong problem.
                return new ApiResponses().BadRequestResult(
                    "This pass's buddy passes aren't set up for any event type yet. An admin needs to finish configuring them.");
            }
            // Phase 1 covers the free perk end to end. A partial discount has to be priced through
            // the counter's ticket path (service charge, admission tax, price ladders), which is a
            // separate piece of work; refusing here is better than silently admitting for free.
            if (!ent.IsFree)
            {
                return new ApiResponses().BadRequestResult(
                    "Discounted buddy passes can't be redeemed at the counter yet — only ones that cover admission outright.");
            }

            var buddy = await _users.GetById(request.BuddyUserId);
            if (buddy is null) return new ApiResponses().NotFoundResult("That guest account doesn't exist.");
            if (buddy.Id == pass.PurchaserUserId)
            {
                return new ApiResponses().BadRequestResult(
                    "A buddy pass is for bringing someone else. The holder is already admitted by their own pass.");
            }

            var tier = await _tiers.GetById(request.TierId, _tenantContext.TenantId);
            if (tier is null || !tier.IsActive) return new ApiResponses().BadRequestResult("That entry option isn't available.");
            var ev = await _events.GetById(tier.EventId, _tenantContext.TenantId);
            if (ev is null || ev.Status != "scheduled" || ev.EndsAt < DateTime.UtcNow)
            {
                return new ApiResponses().BadRequestResult("That event isn't running.");
            }
            if (!ent.Scopes.Any(sc => sc.EventTypeId == ev.EventTypeId))
            {
                var goodFor = string.Join(", ", ent.Scopes
                    .Select(sc => sc.IsWalkUp ? "days with no event" : sc.EventTypeName ?? "an event type"));
                return new ApiResponses().BadRequestResult(
                    $"This pass's buddy passes aren't valid for this kind of event. Good for: {goodFor}.");
            }

            // Everything above is read-only validation. From here the count must not move under
            // us: two registers serving the same family would otherwise both see "1 remaining"
            // and both write. Same per-pass lock contract CreateWalkUpGateCheckIn documents.
            await using var padlock = await _db.AcquireAdvisoryLock($"buddy-pass:{request.PassPurchaseId}");

            var fresh = await _passes.GetBuddyEntitlement(request.PassPurchaseId, _tenantContext.TenantId);
            if (fresh is null || fresh.Remaining <= 0)
            {
                return new ApiResponses().BadRequestResult(
                    $"All {ent.Total} buddy passes on this pass have been used this season.");
            }

            // The buddy is admitted like any other rider: their own ticket, their own QR, and
            // registration still outstanding so the waiver is collected by the normal path rather
            // than skipped. RegistrationComplete=false is what makes the gate ask for it.
            var ticket = new EventTicketPurchase
            {
                TenantId = _tenantContext.TenantId,
                TierId = tier.Id,
                PurchaserUserId = buddy.Id,
                AmountCents = 0,
                ServiceChargeCents = 0,
                TaxCents = 0,
                TaxRateBps = 0,
                PaymentMethod = "voucher",
                Status = "paid",
                PurchaserEmail = buddy.Email,
                PurchaserName = $"{buddy.FirstName} {buddy.LastName}".Trim(),
                SoldByUserId = staffId,
                RegistrationComplete = false,
            };
            var created = await _ticketPurchases.Create(ticket);

            var redemptionId = await _passes.RedeemBuddyPass(new SeasonPassBuddyRedemption
            {
                TenantId = _tenantContext.TenantId,
                PassPurchaseId = request.PassPurchaseId,
                BuddyUserId = buddy.Id,
                EventId = ev.Id,
                TicketPurchaseId = created.Id,
                DiscountCents = tier.PriceCents,
                RedeemedByUserId = staffId,
            });

            await _audit.Log(
                "season_pass.buddy_redeemed",
                $"Redeemed a buddy pass from {pass.PurchaserName}'s pass for {ticket.PurchaserName} at {ev.Title}",
                targetKind: "season_pass_buddy_redemption",
                targetId: redemptionId,
                tenantId: _tenantContext.TenantId,
                metadata: new { passPurchaseId = request.PassPurchaseId, buddyUserId = buddy.Id, eventId = ev.Id, ticketId = created.Id });

            return new ApiResponses().OkResult(new
            {
                redemptionId,
                ticketPurchaseId = created.Id,
                redemptionToken = created.RedemptionToken,
                remaining = fresh.Remaining - 1,
                // The buddy still owes a waiver and rider details before the gate will pass them.
                requiresRegistration = true,
            });
        }

        /// <summary>
        /// Hand a spent buddy credit back. SalesCancel, not SalesCounter: a cashier who can spend
        /// a credit at the window must not also be able to hand it back. Not SalesRefund either,
        /// because no money moves.
        /// </summary>
        [Authorize(Policy = TenantPermissions.Policy.SalesCancel)]
        [HttpPost("Buddy/{redemptionId:guid}/ReturnCredit")]
        public async Task<IActionResult> ReturnBuddyCredit(Guid redemptionId, [FromBody] ReturnBuddyCreditRequest request)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!TryGetUserId(out var adminId)) return new ApiResponses().BadRequestResult("Invalid token.");
            var reason = request.Reason?.Trim();
            if (string.IsNullOrWhiteSpace(reason))
            {
                return new ApiResponses().BadRequestResult("A reason is required to return a buddy credit.");
            }

            var ok = await _passes.ReturnBuddyCredit(redemptionId, _tenantContext.TenantId, adminId, reason);
            if (!ok)
            {
                return new ApiResponses().NotFoundResult("That buddy pass credit was already returned, or isn't from this track.");
            }

            await _audit.Log(
                "season_pass.buddy_credit_returned",
                $"Returned a buddy pass credit — {reason}",
                targetKind: "season_pass_buddy_redemption",
                targetId: redemptionId,
                tenantId: _tenantContext.TenantId,
                metadata: new { reason });

            return new ApiResponses().OkResult();
        }

        [Authorize(Policy = TenantPermissions.Policy.ReportsView)]
        [HttpGet("Buddy/Usage")]
        public async Task<IActionResult> BuddyUsage()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var rows = await _passes.ListBuddyRedemptions(_tenantContext.TenantId);
            // Returned credits are included and flagged, never filtered out: hiding them makes the
            // free admissions they explain look like they came from nowhere.
            return new ApiResponses().OkResult(rows.Select(r => new BuddyRedemptionItem
            {
                Id = r.Id,
                HolderName = r.HolderName,
                BuddyName = string.IsNullOrWhiteSpace(r.BuddyName) ? r.BuddyEmail : r.BuddyName,
                BuddyEmail = r.BuddyEmail,
                EventTitle = r.EventTitle,
                RedeemedAtUtc = DateTime.SpecifyKind(r.RedeemedAt, DateTimeKind.Utc),
                RedeemedByName = string.IsNullOrWhiteSpace(r.RedeemedByName) ? null : r.RedeemedByName,
                CreditReturned = r.CreditReturnedAt.HasValue,
                CreditReturnedAtUtc = r.CreditReturnedAt.HasValue
                    ? DateTime.SpecifyKind(r.CreditReturnedAt.Value, DateTimeKind.Utc) : null,
                CreditReturnedByName = string.IsNullOrWhiteSpace(r.CreditReturnedByName) ? null : r.CreditReturnedByName,
                CreditReturnReason = r.CreditReturnReason,
            }).ToList());
        }

        // ── Employee passes (Script0242) ─────────────────────────────────────────
        // Staff-only passes: granted by an admin, never bought. Gated on UsersManage rather than
        // CatalogManage because issuing one is staff administration that grants free admission,
        // the same bar as disabling a staff account.
        //
        // Eligibility (an active account on this tenant) is automatic and grants nothing.
        // Approval is the deliberate act these endpoints record. Nothing here auto-issues.

        [Authorize(Policy = TenantPermissions.Policy.UsersManage)]
        [HttpGet("Employee/Roster")]
        public async Task<IActionResult> EmployeePassRoster()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var rows = await _passes.ListEmployeePassRoster(_tenantContext.TenantId);
            var products = (await _passes.ListProductsForTenant(
                    _tenantContext.TenantId, activeOnly: false, includeEmployee: true))
                .Where(x => x.IsEmployee)
                .ToList();
            var eventTypes = await _eventTypes.GetAllForTenant(_tenantContext.TenantId);
            var benefitsByProduct = await _passes.ListBenefitsForProducts(
                products.Select(x => x.Id), _tenantContext.TenantId);

            return new ApiResponses().OkResult(new EmployeePassRosterResponse
            {
                Rows = rows.Select(r =>
                {
                    var active = string.Equals(r.EmploymentStatus, "active", StringComparison.OrdinalIgnoreCase);
                    return new EmployeePassRosterItem
                    {
                        UserId = r.UserId,
                        Email = r.Email,
                        Name = string.IsNullOrWhiteSpace(r.Name) ? null : r.Name,
                        Role = r.Role,
                        IsActiveEmployee = active,
                        PassPurchaseId = r.PassPurchaseId,
                        ProductName = r.ProductName,
                        AmountCents = r.AmountCents,
                        ValidFromDate = r.ValidFromDate,
                        ValidToDate = r.ValidToDate,
                        IssuedAtUtc = r.IssuedAtUtc.HasValue
                            ? DateTime.SpecifyKind(r.IssuedAtUtc.Value, DateTimeKind.Utc) : null,
                        IssuedByName = string.IsNullOrWhiteSpace(r.IssuedByName) ? null : r.IssuedByName,
                        // Computed here, not in the page, so the label can never disagree with the
                        // rule the gate actually applies. Order matters: employment first, because
                        // an inactive employee's pass is void whatever else is true of it.
                        PassState =
                            r.PassPurchaseId is null ? "none"
                            : !active ? "inactive_employee"
                            : r.PassStatus == "pending" ? "pending_payment"
                            : !r.IsRegistered ? "not_registered"
                            : "active",
                    };
                }).ToList(),
                Products = products.Select(x => new EmployeePassProductOption
                {
                    Id = x.Id,
                    Name = x.Name,
                    PriceCents = x.PriceCents,
                    ValidFromDate = x.ValidFromDate,
                    ValidToDate = x.ValidToDate,
                    IsActive = x.IsActive,
                    Benefits = benefitsByProduct.TryGetValue(x.Id, out var bs)
                        ? bs.Select(b => new SeasonPassBenefitInput
                        {
                            BenefitType = b.BenefitType,
                            ScopeId = b.ScopeId,
                            DiscountKind = b.DiscountKind,
                            DiscountValue = b.DiscountValue,
                            Quantity = b.Quantity,
                            ScopeName = b.ScopeId.HasValue
                                ? eventTypes.FirstOrDefault(t => t.Id == b.ScopeId.Value)?.Name
                                : null,
                        }).ToList()
                        : new List<SeasonPassBenefitInput>(),
                }).ToList(),
                EventTypes = eventTypes.Select(t => new EmployeeEventTypeOption
                {
                    Id = t.Id,
                    Name = t.Name,
                    Code = t.Code,
                }).ToList(),
                // Honesty about what a configured discount will actually do: offering a control
                // that silently does nothing is worse than not offering it. All four surfaces now
                // read the benefit model at their tills, F&B included (the concession POS resolves
                // a per-pass 'concession' benefit and prefers it over the tenant-wide setting).
                // Kept as a map rather than hardcoded true in the client so a surface can be turned
                // off again without a frontend release.
                SurfaceLive = new Dictionary<string, bool>
                {
                    ["event"] = true,
                    ["retail"] = true,
                    ["rental"] = true,
                    ["concession"] = true,
                },
            });
        }

        [Authorize(Policy = TenantPermissions.Policy.UsersManage)]
        [HttpPost("Employee/Issue")]
        public async Task<IActionResult> IssueEmployeePass([FromBody] IssueEmployeePassRequest request)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!TryGetUserId(out var adminId)) return new ApiResponses().BadRequestResult("Invalid token.");

            var product = await _passes.GetProduct(request.ProductId, _tenantContext.TenantId);
            if (product is null) return new ApiResponses().NotFoundResult("Pass product not found.");
            if (!product.IsEmployee)
            {
                return new ApiResponses().BadRequestResult(
                    "That's a customer pass product. Only an employee pass product can be issued from here.");
            }
            if (!product.IsActive)
            {
                return new ApiResponses().BadRequestResult("That employee pass product is inactive.");
            }

            // Eligibility: an ACTIVE account on THIS tenant. Both halves matter — a rider account
            // (tenant_id null) is not an employee, and a disabled one would receive a pass that
            // could not admit them.
            var employee = await _users.GetById(request.UserId);
            if (employee is null || employee.TenantId != _tenantContext.TenantId)
            {
                return new ApiResponses().NotFoundResult("That person isn't a staff member at this track.");
            }
            if (!string.Equals(employee.Status, "active", StringComparison.OrdinalIgnoreCase))
            {
                return new ApiResponses().BadRequestResult(
                    "That staff account isn't active, so a pass issued to it wouldn't work. Re-activate them first.");
            }
            if (await _passes.HasLiveEmployeePass(request.UserId, _tenantContext.TenantId))
            {
                return new ApiResponses().BadRequestResult("That employee already has a pass. Revoke it first to re-issue.");
            }

            // A free pass is 'paid' with no money path (payment_method 'voucher', the established
            // convention for a zero-value issue). A priced one lands 'pending' and grants nothing
            // until it is settled — every admission path already requires 'paid', so the guard is
            // the existing one rather than a new rule.
            var free = product.PriceCents <= 0;
            var purchase = new SeasonPassPurchase
            {
                TenantId = _tenantContext.TenantId,
                PurchaserUserId = request.UserId,
                ProductId = product.Id,
                IssuedByUserId = adminId,
                AmountCents = product.PriceCents,
                ServiceChargeCents = 0,
                PaymentMethod = "voucher",
                Status = free ? "paid" : "pending",
                PurchaserEmail = employee.Email,
                PurchaserName = $"{employee.FirstName} {employee.LastName}".Trim(),
                ValidFromDate = product.ValidFromDate,
                ValidToDate = product.ValidToDate,
                CreditsRemaining = product.TotalCredits,
            };
            var (purchaseId, token) = await _passes.CreatePurchase(purchase);

            await _audit.Log(
                "season_pass.employee_issued",
                $"Issued the {product.Name} employee pass to {purchase.PurchaserName} ({employee.Email})"
                    + (free ? " (free)" : $" at ${product.PriceCents / 100m:0.00}, awaiting payment"),
                targetKind: "season_pass_purchase",
                targetId: purchaseId,
                tenantId: _tenantContext.TenantId,
                metadata: new { employeeUserId = request.UserId, productId = product.Id, product.PriceCents });

            return new ApiResponses().OkResult(new
            {
                passPurchaseId = purchaseId,
                redemptionToken = token,
                status = purchase.Status,
                // The pass exists but will not scan until the employee adds a photo and signs.
                // Surfaced so the page can say so rather than implying they are good to ride.
                requiresRegistration = true,
            });
        }

        [Authorize(Policy = TenantPermissions.Policy.UsersManage)]
        [HttpPut("Employee/Products/{productId:guid}/Benefits")]
        public async Task<IActionResult> UpdateEmployeeBenefits(
            Guid productId, [FromBody] UpdateEmployeeBenefitsRequest request)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var product = await _passes.GetProduct(productId, _tenantContext.TenantId);
            if (product is null) return new ApiResponses().NotFoundResult("Pass product not found.");
            if (!product.IsEmployee)
            {
                // This endpoint is UsersManage, not CatalogManage. Letting it touch a customer
                // product would be a quiet privilege escalation into the catalog.
                return new ApiResponses().BadRequestResult(
                    "That's a customer pass. Edit its perks under Season Passes.");
            }

            var rows = new List<SeasonPassBenefit>();
            foreach (var b in request.Benefits)
            {
                // A zero-value discount is a row that grants nothing; drop it rather than storing
                // noise the tills have to evaluate. Buddy passes are the exception (quantity is
                // the grant), but they are not offered here.
                if (b.BenefitType == "buddy_pass") continue;
                if (b.DiscountValue <= 0) continue;
                if (b.DiscountKind == "percent" && b.DiscountValue > 10000)
                {
                    return new ApiResponses().BadRequestResult("A percentage discount can't exceed 100%.");
                }
                rows.Add(new SeasonPassBenefit
                {
                    TenantId = _tenantContext.TenantId,
                    PassProductId = productId,
                    BenefitType = b.BenefitType,
                    ScopeId = b.BenefitType == "event" ? b.ScopeId : null,
                    DiscountKind = b.DiscountKind,
                    DiscountValue = b.DiscountValue,
                    Quantity = null,
                });
            }

            await _passes.ReplaceBenefits(productId, _tenantContext.TenantId, rows);
            await _audit.Log(
                "season_pass.employee_perks_updated",
                $"Updated employee perks on {product.Name} ({rows.Count} in effect)",
                targetKind: "season_pass_product",
                targetId: productId,
                tenantId: _tenantContext.TenantId,
                metadata: new { benefits = rows.Select(r => new { r.BenefitType, r.ScopeId, r.DiscountKind, r.DiscountValue }) });

            return new ApiResponses().OkResult();
        }

        [Authorize(Policy = TenantPermissions.Policy.UsersManage)]
        [HttpPost("Employee/{purchaseId:guid}/Revoke")]
        public async Task<IActionResult> RevokeEmployeePass(Guid purchaseId, [FromBody] RevokeEmployeePassRequest request)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!TryGetUserId(out var adminId)) return new ApiResponses().BadRequestResult("Invalid token.");
            var reason = request.Reason?.Trim();
            if (string.IsNullOrWhiteSpace(reason))
            {
                return new ApiResponses().BadRequestResult("A reason is required to revoke an employee pass.");
            }

            var ok = await _passes.RevokeEmployeePass(purchaseId, _tenantContext.TenantId, adminId, reason);
            if (!ok)
            {
                return new ApiResponses().NotFoundResult("That employee pass no longer exists or was already revoked.");
            }

            await _audit.Log(
                "season_pass.employee_revoked",
                $"Revoked an employee pass — {reason}",
                targetKind: "season_pass_purchase",
                targetId: purchaseId,
                tenantId: _tenantContext.TenantId,
                metadata: new { reason });

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
                // Employee passes are granted by an admin, never bought. Rejected here as well as
                // hidden from the product list, because this endpoint takes a product id straight
                // from the request and hiding a thing is not the same as refusing it.
                if (p.IsEmployee)
                {
                    return new ApiResponses().BadRequestResult("This pass isn't available for purchase.");
                }
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
            // Employee pass (Script0242): holding capacity with a pass that cannot admit you is
            // worse than a plain refusal, because the slot is gone for someone who could use it.
            // The caller is the holder (checked above), so their own account decides.
            if (product.IsEmployee)
            {
                var holder = await _users.GetById(userId);
                if (holder is null || holder.TenantId != _tenantContext.TenantId
                    || !string.Equals(holder.Status, "active", StringComparison.OrdinalIgnoreCase))
                {
                    return new ApiResponses().BadRequestResult(
                        "This employee pass is no longer active. Check with the track office.");
                }
            }
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

            // Burn BEFORE creating the reservation: if the last credit was raced away since the
            // check above, no reservation must exist to admit anyone. (The old order created the
            // reservation first and ignored the burn result — a raced pass could over-reserve.)
            if (product.Kind == "credits")
            {
                var burned = await _passes.TryDecrementCredits(pass.Id, _tenantContext.TenantId);
                if (burned == 0)
                {
                    return new ApiResponses().BadRequestResult("This pass has no credits remaining.");
                }
            }
            Guid reservationId;
            if (existing is not null)
            {
                // A cancelled prior reservation blocks the (pass, event) UNIQUE — revive it
                // instead of inserting a duplicate.
                await _passes.UpdateReservationStatus(existing.Id, _tenantContext.TenantId, "reserved");
                reservationId = existing.Id;
            }
            else
            {
                reservationId = await _passes.CreateReservation(new SeasonPassReservation
                {
                    SeasonPassPurchaseId = request.PassPurchaseId,
                    EventId = request.EventId,
                    Status = "reserved",
                });
            }
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
            var reservations = await _passes.ListReservationsForPurchaseOnDate(
                pass.Id, _tenantContext.TenantId, atUtc, untilUtc, dayStartLocal);

            // Today's admissible events, so the scanner can offer walk-up redemption without
            // the pass holder having pre-booked anything (0 → nothing running today, 1 →
            // auto-selected, >1 → staff pick).
            var todaysEvents = (await _events.GetInRange(_tenantContext.TenantId, atUtc, untilUtc))
                .Where(e => e.Status == "scheduled")
                .ToList();

            // Registration state up front so staff see "not registered yet" on the scan rather than
            // only when the check-in button fails.
            var registrationComplete = pass.IsRegistered(product?.RequiresWaiver ?? false);

            // ── The two things the gate worker has to see before banding someone ──────────
            // Waiver. Computed the same way RedeemPassAtGate ENFORCES it, so the tick on screen
            // and the block on the button can never disagree: a signature on the pass itself
            // satisfies it outright, otherwise the event's own rider waiver decides. With no
            // event picked yet (nothing running, or several to choose from) there is no event
            // waiver to resolve, so fall back to the pass signature and leave the reason null
            // rather than inventing a block staff can't act on.
            var ctx = await _passes.GetPassForGateCheckIn(pass.Id, _tenantContext.TenantId);
            // Already through the gate today settles it: admission enforces the waiver on every
            // path, so a checked_in reservation IS the evidence. Without this, a rider who
            // satisfied it via the event's waiver rather than a signature on the pass would read
            // as "not signed" for the rest of the day.
            var admittedToday = reservations.Any(r => r.Status == "checked_in");
            var waiverSigned = admittedToday || ctx?.WaiverSignatureId is not null;
            string? waiverBlockReason = null;
            if (!waiverSigned && ctx is not null && todaysEvents.Count == 1)
            {
                waiverBlockReason = await _waiverGate.BlockReason(
                    _tenantContext.TenantId, todaysEvents[0].Id, riderAudience: true,
                    ctx.HolderUserId, ctx.HolderEmail, ctx.HolderName);
                waiverSigned = waiverBlockReason is null;
            }

            // ID / age. Resolved through the shared service so this display and the wristband
            // gate always agree about who counts as verified.
            var idStatus = await _idVerification.StatusForPass(pass, _tenantContext.TenantId);
            // Buddy entitlement (Script0247). Null for a product that grants none, which is most
            // of them, so the scan screen shows the panel only when there is something to spend.
            var buddy = await _passes.GetBuddyEntitlement(pass.Id, _tenantContext.TenantId);

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
                WaiverSigned = waiverSigned,
                WaiverBlockReason = waiverBlockReason,
                // Employee pass whose holder is no longer active staff (Script0242). Surfaced as
                // its own reason rather than letting the scan look fine and the button fail: the
                // worker has to be able to tell the person WHY they are not getting in.
                EmployeePassBlockReason = ctx is { IsEmployeePass: true, EmployeeEligible: false }
                    ? "This is a staff pass and the employee is no longer active. It can't be used."
                    : null,
                // What this holder can spend on a guest, so the counter can offer it on the same
                // scan that proved the holder is standing there.
                BuddyPass = buddy is null ? null : new BuddyEntitlementResponse
                {
                    Total = buddy.Total,
                    Used = buddy.Used,
                    Remaining = buddy.Remaining,
                    IsFree = buddy.IsFree,
                    DiscountKind = buddy.DiscountKind,
                    DiscountValue = buddy.DiscountValue,
                    GoodFor = buddy.Scopes
                        .Select(sc => sc.IsWalkUp ? "Days with no event" : sc.EventTypeName ?? "An event type")
                        .ToList(),
                    EventTypeIds = buddy.Scopes.Where(sc => sc.EventTypeId.HasValue)
                        .Select(sc => sc.EventTypeId!.Value).ToList(),
                    CoversWalkUpDays = buddy.Scopes.Any(sc => sc.IsWalkUp),
                },
                IdVerified = idStatus.Verified,
                IdVerifiedAtUtc = idStatus.VerifiedAtUtc,
                IdVerifiedByName = idStatus.VerifiedByName,
                IdVerifiedScope = idStatus.Scope,
                // Age off the DOCUMENT, not the sign-up form. Null until verified, so the screen
                // can never present a typed-in age as a checked one.
                IdVerifiedAge = idStatus.VerifiedAge,
                // Self-reported, shown as the starting point for the verify dialog.
                HolderBirthdate = pass.HolderBirthdate,
                // Echoed so the scanner knows whether to enforce, without a second round trip.
                RequireIdForWristband = _tenantContext.Tenant.RequireIdForWristband,
                // Who the pass admits — may differ from the buyer, so this is the name staff
                // should be checking against the photo.
                HolderName = string.IsNullOrWhiteSpace(pass.HolderFirstName)
                    ? null
                    : $"{pass.HolderFirstName} {pass.HolderLastName}".Trim(),
                RegistrationComplete = registrationComplete,
                ProductName = product?.Name,
                ProductKind = product?.Kind,
                ProductTotalCredits = product?.TotalCredits,
                ValidDaysOfWeek = product?.ValidDaysOfWeek,
                TodaysEvents = todaysEvents.Select(e => new
                {
                    e.Id,
                    e.Title,
                    // Carried so the buddy flow can offer only the events this holder's
                    // entitlement is good for, instead of listing all of today's and failing
                    // at redeem with "not valid for this kind of event".
                    e.EventTypeId,
                    StartsAtUtc = DateTime.SpecifyKind(e.StartsAt, DateTimeKind.Utc),
                    EndsAtUtc = DateTime.SpecifyKind(e.EndsAt, DateTimeKind.Utc),
                }),
                // A no-event walk-up admission has no event to describe, so it carries a stand-in
                // title and null times; the scanner renders it from CheckInDate instead.
                TodaysReservations = reservations.Select(r => new
                {
                    r.Id,
                    r.EventId,
                    EventTitle = r.EventTitle ?? "Walk-up admission",
                    EventStartsAtUtc = r.EventStartsAt is null
                        ? null : (DateTime?)DateTime.SpecifyKind(r.EventStartsAt.Value, DateTimeKind.Utc),
                    EventEndsAtUtc = r.EventEndsAt is null
                        ? null : (DateTime?)DateTime.SpecifyKind(r.EventEndsAt.Value, DateTimeKind.Utc),
                    r.CheckInDate,
                    r.Status,
                    CheckedInAtUtc = r.CheckedInAt is null ? null : (DateTime?)DateTime.SpecifyKind(r.CheckedInAt.Value, DateTimeKind.Utc),
                }),
            });
        }

        /// <summary>
        /// Records that a gate worker checked this holder's photo ID and date of birth. SalesRedeem
        /// because this is a counter action performed with the rider standing there, the same
        /// permission that admits them.
        ///
        /// The result persists, which is the whole point: unlike tenant.require_id_at_checkin (a
        /// per-scan attestation that records nothing), a rider is carded once and every later scan
        /// shows the tick. It lands on the pass always, and on the rider's account too when the
        /// buyer IS the holder, never on a parent's account for their child's pass.
        /// </summary>
        [Authorize(Policy = TenantPermissions.Policy.SalesRedeem)]
        [HttpPost("Pass/{token:guid}/VerifyId")]
        public async Task<IActionResult> VerifyPassHolderId(Guid token, [FromBody] VerifyRiderIdRequest request)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");

            var pass = await _passes.GetPurchaseByRedemptionToken(token);
            if (pass is null || pass.TenantId != _tenantContext.TenantId)
                return new ApiResponses().NotFoundResult("Pass not found.");
            if (pass.Status != "paid")
                return new ApiResponses().BadRequestResult(
                    $"This pass is {pass.Status}, so there's nothing to verify against. Sort the pass out first.");

            // The DOB is the age evidence, so a verification without one is not worth recording.
            var dob = request.VerifiedDob ?? pass.HolderBirthdate;
            if (dob is null)
                return new ApiResponses().BadRequestResult(
                    "Enter the date of birth from the rider's ID. It's what the age check rests on.");
            if (dob.Value.Date > DateTime.UtcNow.Date)
                return new ApiResponses().BadRequestResult("That date of birth is in the future. Check the ID again.");

            var staffId = TryGetUserId(out var sid) ? sid : (Guid?)null;
            var status = await _idVerification.RecordForPass(pass, _tenantContext.TenantId, staffId, dob);
            if (!status.Verified)
                return new ApiResponses().BadRequestResult(
                    "The verification did not save: the pass changed while you were entering it. Rescan and try again.");

            var holder = string.IsNullOrWhiteSpace(pass.HolderFirstName)
                ? pass.PurchaserName
                : $"{pass.HolderFirstName} {pass.HolderLastName}".Trim();
            await _audit.Log("rider.id_verified",
                $"Verified ID and age for {holder} (age {status.VerifiedAge}).",
                targetKind: "season_pass_purchase", targetId: pass.Id,
                tenantId: _tenantContext.TenantId,
                metadata: new { scope = status.Scope, verifiedDob = dob.Value.Date });

            return new ApiResponses().OkResult(new
            {
                IdVerified = true,
                IdVerifiedAtUtc = status.VerifiedAtUtc,
                IdVerifiedByName = status.VerifiedByName,
                IdVerifiedScope = status.Scope,
                IdVerifiedAge = status.VerifiedAge,
            });
        }

        /// <summary>
        /// Undoes a verification recorded in error. UsersManage rather than SalesRedeem: a gate
        /// worker records the check, but unwinding a compliance record is an administrative act.
        /// Clears the account as well as the pass, since leaving either behind would keep
        /// answering "verified".
        /// </summary>
        [Authorize(Policy = TenantPermissions.Policy.UsersManage)]
        [HttpPost("Pass/{token:guid}/ClearIdVerification")]
        public async Task<IActionResult> ClearPassHolderIdVerification(Guid token)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");

            var pass = await _passes.GetPurchaseByRedemptionToken(token);
            if (pass is null || pass.TenantId != _tenantContext.TenantId)
                return new ApiResponses().NotFoundResult("Pass not found.");

            await _idVerification.ClearForPass(pass, _tenantContext.TenantId);

            var holder = string.IsNullOrWhiteSpace(pass.HolderFirstName)
                ? pass.PurchaserName
                : $"{pass.HolderFirstName} {pass.HolderLastName}".Trim();
            await _audit.Log("rider.id_verification_cleared",
                $"Cleared the ID/age verification for {holder}.",
                targetKind: "season_pass_purchase", targetId: pass.Id,
                tenantId: _tenantContext.TenantId);

            return new ApiResponses().OkResult(new { IdVerified = false });
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

        /// <summary>
        /// Gate redemption: staff scanned a pass QR. What happens next depends on the tenant's
        /// admission mode (tenant.season_pass_admission_type_id).
        ///
        /// WalkUp: the pass admits on scan alone, against one of today's events when any are
        /// running, or against the tenant-local operating day when the calendar is empty. For
        /// credits passes this burns one ride credit atomically with the check-in record;
        /// unlimited and day-of-week passes go through the same door without a burn.
        ///
        /// EventSignUp: the holder must already hold a reservation for the event. A scan with no
        /// event, or with no live reservation for that event, is refused and told to sign up.
        /// </summary>
        [Authorize(Policy = TenantPermissions.Policy.SalesRedeem)]
        [HttpPost("Pass/{token:guid}/Redeem")]
        public async Task<IActionResult> RedeemPassAtGate(Guid token, [FromBody] SeasonPassGateRedeemRequest request)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            Guid? staffId = TryGetUserId(out var sid) ? sid : (Guid?)null;

            var pass = await _passes.GetPurchaseByRedemptionToken(token);
            if (pass is null || pass.TenantId != _tenantContext.TenantId)
            {
                // Same shape as the lookup: don't reveal that the token exists on another tenant.
                return new ApiResponses().NotFoundResult("Pass not found.");
            }
            if (pass.Status == "pending")
                return new ApiResponses().BadRequestResult("This pass's payment hasn't settled yet, so it can't be used.");
            if (pass.Status != "paid")
                return new ApiResponses().BadRequestResult("This pass was refunded or cancelled and is no longer valid.");

            var product = await _passes.GetProduct(pass.ProductId, _tenantContext.TenantId);
            if (product is null) return new ApiResponses().BadRequestResult("Pass product missing — contact support.");

            // Which admission model this track runs.
            var admissionType = (SeasonPassAdmissionType)_tenantContext.Tenant.SeasonPassAdmissionTypeId;

            var tz = TimeZoneInfo.FindSystemTimeZoneById(_tenantContext.Tenant.Timezone);
            var todayLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).Date;

            // A sign-up track has no admission path without an event, so reject the no-event shape
            // before any branching rather than letting it fall through to the walk-up anchor.
            if (admissionType == SeasonPassAdmissionType.EventSignUp && request.EventId is null)
            {
                return new ApiResponses().BadRequestResult(
                    "This track requires an event sign-up before riding. The rider must reserve a spot " +
                    "for the event from My Passes first, then scan again.");
            }

            // The operating day this scan validates against: the event's day, or simply today.
            DateTime dayLocal;
            var eventId = request.EventId;

            if (eventId is Guid evId)
            {
                var ev = await _events.GetById(evId, _tenantContext.TenantId);
                if (ev is null || ev.Status != "scheduled")
                    return new ApiResponses().BadRequestResult("That event isn't available for check-in.");

                // Same-day only in the tenant's timezone. Advance booking stays on Reserve.
                var eventDayLocal = TimeZoneInfo.ConvertTimeFromUtc(
                    DateTime.SpecifyKind(ev.StartsAt, DateTimeKind.Utc), tz).Date;
                if (eventDayLocal != todayLocal)
                    return new ApiResponses().BadRequestResult("That event isn't running today, and gate redemption is same-day only.");
                dayLocal = eventDayLocal;
            }
            else
            {
                // Walk-up track with an empty calendar: the operating day is the anchor.
                dayLocal = todayLocal;
            }

            if (dayLocal < pass.ValidFromDate.Date)
                return new ApiResponses().BadRequestResult($"This pass's season hasn't started yet, it's valid from {pass.ValidFromDate:MMM d, yyyy}.");
            if (dayLocal > pass.ValidToDate.Date)
                return new ApiResponses().BadRequestResult($"This pass's season ended {pass.ValidToDate:MMM d, yyyy}.");

            if (product.Kind == "days_of_week" && product.ValidDaysOfWeek is { Length: > 0 })
            {
                var dow = (int)dayLocal.DayOfWeek;     // 0=Sun..6=Sat
                if (!product.ValidDaysOfWeek.Contains(dow))
                    return new ApiResponses().BadRequestResult("This pass isn't valid on this day of the week.");
            }

            // Registration gate (photo to verify the holder, product waiver when required),
            // then the event's own rider waiver, same standards as reservation check-in.
            var ctx = await _passes.GetPassForGateCheckIn(pass.Id, _tenantContext.TenantId);
            if (ctx is not null)
            {
                var registrationBlock = await RegistrationBlockReason(ctx);
                if (registrationBlock is not null) return new ApiResponses().BadRequestResult(registrationBlock);

                // Skipped with no event: there is no event document to carry a rider waiver, so
                // there is nothing to enforce. The product's own waiver was covered just above.
                if (eventId is Guid waiverEventId && ctx.WaiverSignatureId is null)
                {
                    var waiverBlock = await _waiverGate.BlockReason(_tenantContext.TenantId, waiverEventId,
                        riderAudience: true, ctx.HolderUserId, ctx.HolderEmail, ctx.HolderName);
                    if (waiverBlock is not null) return new ApiResponses().BadRequestResult(waiverBlock);
                }
            }

            // Serialize double-scans of the same pass; the pre-checks below and the atomic
            // burn/upsert in both CreateGateCheckIn and CreateWalkUpGateCheckIn depend on it.
            // Keyed on the pass alone, so it covers every anchor that pass could be admitted
            // against today, event or no event.
            await using var redeemLock = await _db.AcquireAdvisoryLock($"season-pass-redeem:{pass.Id}");

            if (eventId is Guid gateEventId)
            {
                var existing = await _passes.GetReservation(pass.Id, gateEventId);

                // A sign-up track admits only a pass that reserved ahead. Re-checked here, under
                // the lock, so a raced cancel can't slip a walk-up through after the earlier checks.
                if (admissionType == SeasonPassAdmissionType.EventSignUp
                    && existing?.Status is not ("reserved" or "checked_in"))
                {
                    return new ApiResponses().BadRequestResult(
                        "This track requires an event sign-up before riding. The rider must reserve a spot " +
                        "for the event from My Passes first, then scan again.");
                }

                if (existing is not null && existing.Status == "checked_in")
                {
                    return new ApiResponses().OkResult(new
                    {
                        ReservationId = existing.Id,
                        AlreadyAdmitted = true,
                        CheckedInAtUtc = existing.CheckedInAt is null
                            ? null : (DateTime?)DateTime.SpecifyKind(existing.CheckedInAt.Value, DateTimeKind.Utc),
                        pass.CreditsRemaining,
                    });
                }
                if (existing is not null && existing.Status == "reserved")
                {
                    // Pre-booked via Reserve, which already burned the credit, so just flip it.
                    // On a sign-up track this IS the admission; CreateGateCheckIn is never reached.
                    var flipped = await _passes.UpdateReservationStatus(existing.Id, _tenantContext.TenantId, "checked_in", staffId);
                    if (flipped == 0)
                        return new ApiResponses().BadRequestResult(
                            "This pass can't be checked in. It may be refunded, cancelled, or already checked in.");
                    return new ApiResponses().OkResult(new
                    {
                        ReservationId = existing.Id,
                        AlreadyAdmitted = false,
                        CheckedInAtUtc = (DateTime?)DateTime.UtcNow,
                        pass.CreditsRemaining,
                    });
                }

                // Walk-up only past this point: a sign-up track already returned above.
                var burnCredit = product.Kind == "credits";
                var result = await _passes.CreateGateCheckIn(pass.Id, _tenantContext.TenantId, gateEventId, staffId, burnCredit);
                if (result is null)
                {
                    // Two different things produce null: no credits left, or a live reservation
                    // appeared between the pre-check above and the write (the statement's own
                    // guard against burning a second credit). Telling a rider with a full pass
                    // that they have no rides left would send them to the office over nothing, so
                    // re-read before choosing the message. Only on this rare path, never per scan.
                    var raced = await _passes.GetReservation(pass.Id, gateEventId);
                    if (raced is not null && raced.Status == "checked_in")
                    {
                        return new ApiResponses().OkResult(new
                        {
                            ReservationId = raced.Id,
                            AlreadyAdmitted = true,
                            CheckedInAtUtc = raced.CheckedInAt is null
                                ? null : (DateTime?)DateTime.SpecifyKind(raced.CheckedInAt.Value, DateTimeKind.Utc),
                            pass.CreditsRemaining,
                        });
                    }
                    return new ApiResponses().BadRequestResult(
                        "This pass has no ride credits left. If that's a mistake, credits can be adjusted from the customer's admin page.");
                }
                return new ApiResponses().OkResult(new
                {
                    ReservationId = result.Value.ReservationId,
                    AlreadyAdmitted = false,
                    CheckedInAtUtc = (DateTime?)DateTime.UtcNow,
                    CreditsRemaining = result.Value.CreditsRemaining,
                });
            }
            else
            {
                // No event on the calendar: the anchor is (pass, today's local date). This
                // pre-check is the fast path; CreateWalkUpGateCheckIn now refuses to burn against
                // a live admission on its own, so a race here costs a wasted round trip rather
                // than a rider's credit.
                var existingWalkUp = await _passes.GetWalkUpCheckIn(pass.Id, _tenantContext.TenantId, dayLocal);
                if (existingWalkUp is not null && existingWalkUp.Status == "checked_in")
                {
                    return new ApiResponses().OkResult(new
                    {
                        ReservationId = existingWalkUp.Id,
                        AlreadyAdmitted = true,
                        CheckedInAtUtc = existingWalkUp.CheckedInAt is null
                            ? null : (DateTime?)DateTime.SpecifyKind(existingWalkUp.CheckedInAt.Value, DateTimeKind.Utc),
                        pass.CreditsRemaining,
                    });
                }

                var burnCredit = product.Kind == "credits";
                var result = await _passes.CreateWalkUpGateCheckIn(pass.Id, _tenantContext.TenantId, dayLocal, staffId, burnCredit);
                if (result is null)
                {
                    // Same two causes as the event branch, same re-read before blaming credits.
                    var raced = await _passes.GetWalkUpCheckIn(pass.Id, _tenantContext.TenantId, dayLocal);
                    if (raced is not null && raced.Status == "checked_in")
                    {
                        return new ApiResponses().OkResult(new
                        {
                            ReservationId = raced.Id,
                            AlreadyAdmitted = true,
                            CheckedInAtUtc = raced.CheckedInAt is null
                                ? null : (DateTime?)DateTime.SpecifyKind(raced.CheckedInAt.Value, DateTimeKind.Utc),
                            pass.CreditsRemaining,
                        });
                    }
                    return new ApiResponses().BadRequestResult(
                        "This pass has no ride credits left. If that's a mistake, credits can be adjusted from the customer's admin page.");
                }
                return new ApiResponses().OkResult(new
                {
                    ReservationId = result.Value.ReservationId,
                    AlreadyAdmitted = false,
                    CheckedInAtUtc = (DateTime?)DateTime.UtcNow,
                    CreditsRemaining = result.Value.CreditsRemaining,
                });
            }
        }

        /// <summary>
        /// Admin support override of a credits pass's remaining rides. SalesRefund policy:
        /// handing rides back is economically a refund-shaped action, and that policy carries
        /// the right admin/manager blast radius.
        /// </summary>
        [Authorize(Policy = TenantPermissions.Policy.SalesRefund)]
        [HttpPut("Admin/Purchases/{id:guid}/Credits")]
        public async Task<IActionResult> AdjustCredits(Guid id, [FromBody] AdjustSeasonPassCreditsRequest request)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (string.IsNullOrWhiteSpace(request.Reason))
                return new ApiResponses().BadRequestResult("A reason is required — credit adjustments are audit-logged.");

            var pass = await _passes.GetPurchase(id);
            if (pass is null || pass.TenantId != _tenantContext.TenantId)
                return new ApiResponses().NotFoundResult("Season pass not found.");
            var product = await _passes.GetProduct(pass.ProductId, _tenantContext.TenantId);
            if (product?.Kind != "credits")
                return new ApiResponses().BadRequestResult("Only credit-based passes have an adjustable ride count.");

            var previous = pass.CreditsRemaining;
            var affected = await _passes.SetCredits(id, _tenantContext.TenantId, request.CreditsRemaining);
            if (affected == 0)
                return new ApiResponses().BadRequestResult("This pass's credits couldn't be updated — it may not be a credits pass.");

            await _audit.Log(
                action: "season_pass.credits_adjusted",
                summary: $"Set credits on '{product.Name}' pass for {pass.PurchaserEmail} from {previous?.ToString() ?? "—"} to {request.CreditsRemaining}: {request.Reason.Trim()}",
                targetKind: "season_pass_purchase",
                targetId: id,
                tenantId: _tenantContext.TenantId,
                metadata: new { previous, request.CreditsRemaining, reason = request.Reason.Trim() });

            return new ApiResponses().OkResult(new { CreditsRemaining = request.CreditsRemaining });
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
            // Buddy scopes hang off the buddy benefit, so resolve them per product that has one.
            // Products per tenant are few and only some carry a buddy perk, so this is a handful
            // of lookups rather than something worth a bulk query.
            var buddyScopes = new Dictionary<Guid, List<SeasonPassBuddyScope>>();
            foreach (var (pid, bs) in benefitsByProduct)
            {
                var buddy = bs.FirstOrDefault(b => b.BenefitType == "buddy_pass");
                if (buddy is not null) buddyScopes[pid] = await _passes.ListBuddyScopes(buddy.Id, _tenantContext.TenantId);
            }

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
                    IsEmployee = p.IsEmployee,
                    BuddyEventTypeIds = buddyScopes.TryGetValue(p.Id, out var bsc)
                        ? bsc.Where(x => x.EventTypeId.HasValue).Select(x => x.EventTypeId!.Value).ToList()
                        : new List<Guid>(),
                    BuddyIncludeWalkUp = buddyScopes.TryGetValue(p.Id, out var bsc2) && bsc2.Any(x => x.IsWalkUp),
                    SortOrder = p.SortOrder,
                    Slug = p.Slug,
                    HeroImageUrl = p.HeroImageUrl,
                    LandingHtml = p.LandingHtml,
                    LandingPublished = p.LandingPublished,
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
