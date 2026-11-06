using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.BikeShop;
using Services.Helpers;
using Services.Repositories.Data.BikeShopData;
using Services.Repositories.Interfaces;
using Services.Storage;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.BikeShop;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    // Used & ex-rental listings, staff side (Script0302): put one serialized unit on the sales
    // floor at its own asking price, with a condition grade and its own photos; take it back to the
    // fleet; and flag a product as used so the storefront badges it. ShopCounter throughout, like
    // the rest of the shop floor; only the product-level condition is a catalog edit (CatalogManage).
    //
    // A listing never owns the unit's sale. The register and the public store sell it through the
    // ordinary shop_sale path (the line names the unit), and the generic sale-complete path flips
    // the unit to 'sold'; this controller just reads that back as the listing's state.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = TenantPermissions.Policy.ShopCounter)]
    public class ShopListingController : ControllerBase
    {
        private const int MaxPhotos = 12;
        private const long MaxBytes = 5 * 1024 * 1024;

        private static readonly Dictionary<string, string> AllowedTypes =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["image/png"] = ".png",
                ["image/jpeg"] = ".jpg",
                ["image/webp"] = ".webp",
            };

        private readonly IShopListingRepository _listings;
        private readonly IListingReservationService _reservations;
        private readonly IListingSaleGuard _saleGuard;
        private readonly Services.Helpers.Interfaces.IDbHelper _db;
        private readonly IImageStorage _imageStorage;
        private readonly ITenantContext _tenantContext;

        public ShopListingController(IShopListingRepository listings, IListingReservationService reservations,
            IListingSaleGuard saleGuard, Services.Helpers.Interfaces.IDbHelper db, IImageStorage imageStorage,
            ITenantContext tenantContext)
        {
            _listings = listings;
            _reservations = reservations;
            _saleGuard = saleGuard;
            _db = db;
            _imageStorage = imageStorage;
            _tenantContext = tenantContext;
        }

        private const string EarlierPaymentUnconfirmedMessage =
            "An earlier card payment for this bike hasn't been confirmed cancelled yet. Try again in a few minutes.";
        private const string EarlierPaymentWentThroughMessage =
            "An earlier payment for this bike went through. Check that sale before listing it again.";

        /// <summary>Script0303's trigger (a listed unit moved off the floor outside the listing code)
        /// surfaces as a 409 with its own copy rather than a 500.</summary>
        private static bool IsListedUnitGuard(Exception ex) =>
            ex is Npgsql.PostgresException pg && pg.SqlState == ListingRules.ListedUnitSqlState;

        // The same per-listing lock the online checkout takes, so a hold cannot be placed between
        // this request's release and its write.
        private Task<IAsyncDisposable> ListingLock(Guid listingId) => _db.AcquireAdvisoryLock($"used-listing:{listingId}");

        private const string SoldOnlineMessage = "This bike was just sold online; the sale is completing.";
        private const string PaymentInFlightMessage = "An online payment for this bike is still in flight. Try again in a few minutes.";

        private Guid TenantId => _tenantContext.TenantId;
        private Guid? UserId => Guid.TryParse(User.FindFirst("UserId")?.Value, out var id) ? id : null;
        private bool NoTenant => !_tenantContext.IsResolved;

        // ── Reads ─────────────────────────────────────────────────────────────────
        /// <summary>status = active | sold | unlisted | all (default all).</summary>
        [HttpGet("")]
        public async Task<IActionResult> List([FromQuery] string? status = null)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var filter = (status ?? "all").Trim().ToLowerInvariant();
            if (filter is not ("active" or "sold" or "unlisted" or "all"))
                return new ApiResponses().BadRequestResult("status must be active, sold, unlisted or all.");
            var now = DateTime.UtcNow;
            var rows = await _listings.List(TenantId, filter);
            return new ApiResponses().OkResult(new { listings = rows.Select(r => ToDto(r, now)) });
        }

        /// <summary>
        /// Open listings for these units, keyed by unit id, so the register's unit picker can show
        /// "For sale · $1,800 · Good" and what the server will ring. ids = comma-separated guids
        /// (ids=a,b,c), or repeated (ids=a&amp;ids=b), or both.
        /// </summary>
        [HttpGet("ForItems")]
        public async Task<IActionResult> ForItems([FromQuery] string[]? ids)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var itemIds = (ids ?? Array.Empty<string>())
                .SelectMany(s => (s ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Select(s => Guid.TryParse(s, out var g) ? g : (Guid?)null)
                .Where(g => g is not null)
                .Select(g => g!.Value)
                .ToList();
            if (itemIds.Count == 0) return new ApiResponses().OkResult(new Dictionary<Guid, object>());
            var now = DateTime.UtcNow;
            var rows = await _listings.ListActiveForItems(itemIds, TenantId);
            var byItem = rows.ToDictionary(r => r.ItemId, r => (object)new
            {
                listingId = r.Id,
                askingPriceCents = r.AskingPriceCents,
                conditionGrade = r.ConditionGrade,
                title = r.Title,
                published = r.Published,
                reserved = ListingRules.IsReservationLive(r.ReservedSaleId, r.ReservedAt, r.ReservedSaleStatus, now),
            });
            return new ApiResponses().OkResult(byItem);
        }

        /// <summary>
        /// Every product at the tenant marked used, active or not, published or not, as
        /// { "productId": "used" }, for the admin's Used pill. Products not in the map are new.
        /// </summary>
        [HttpGet("ProductConditions")]
        public async Task<IActionResult> ProductConditions()
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            return new ApiResponses().OkResult(await _listings.ListProductConditions(TenantId));
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> Get(Guid id)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var row = await _listings.Get(id, TenantId);
            if (row is null) return new ApiResponses().NotFoundResult("Listing not found.");
            return new ApiResponses().OkResult(ToDto(row, DateTime.UtcNow));
        }

        // ── Listing writes ────────────────────────────────────────────────────────
        [HttpPost("")]
        public async Task<IActionResult> Create([FromBody] CreateShopListingRequest req)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!_tenantContext.Tenant.BikeShopEnabled)
                return new ApiResponses().BadRequestResult("The bike shop isn't turned on for this track.");
            if (ListingRules.ValidatePrice(req.AskingPriceCents) is string priceError)
                return new ApiResponses().BadRequestResult(priceError);
            var grade = req.ConditionGrade?.Trim().ToLowerInvariant();
            if (!ListingRules.IsValidGrade(grade))
                return new ApiResponses().BadRequestResult("Condition grade must be excellent, good or fair.");

            var ctx = await _listings.GetItemContext(req.ItemId, TenantId);
            if (ctx is null) return new ApiResponses().NotFoundResult("Unit not found.");

            var title = Blank(req.Title) ?? ListingRules.DefaultTitle(
                ctx.ProductName, ListingRules.VariantLabel(ctx.Size, ctx.Color, ctx.Gender), ctx.UnitLabel);

            // A stranded open row (its unit left the floor some other way) is closed by the create
            // below. If an online checkout still holds it, live or stale, that checkout is RELEASED
            // first, the one way a hold is ever cleared: the unit is not on the floor, so nobody can
            // be allowed to finish paying for it. A paid one means the bike is sold: refuse.
            await using var strandedLock = await LockStrandedHold(req.ItemId, ctx.ItemStatus);
            var open = strandedLock is null ? null : await _listings.GetOpenForItem(req.ItemId, TenantId);
            if (open is not null && open.ItemStatus != ListingRules.ForSale && open.ReservedSaleId is not null)
            {
                var release = await _reservations.Release(open, HttpContext.RequestAborted);
                if (release == ReservationRelease.AlreadyPaid) return new ApiResponses().ConflictResult(SoldOnlineMessage);
                if (release == ReservationRelease.Blocked) return new ApiResponses().ConflictResult(PaymentInFlightMessage);
            }

            // Sales that died naming this unit (a declined register card sale, an abandoned checkout,
            // a refund) would make its next sale fail on uk_shop_sale_line_item: let them go first,
            // each one only once its PaymentIntent can never be confirmed. Only for a unit the create
            // will accept; a rented-out or sold unit is refused below without touching its history.
            if (ListingRules.CanList(ctx.ItemStatus))
            {
                var freed = await _saleGuard.FreeUnit(req.ItemId, TenantId, "list", HttpContext.RequestAborted);
                if (freed == ReservationRelease.AlreadyPaid) return new ApiResponses().ConflictResult(EarlierPaymentWentThroughMessage);
                if (freed == ReservationRelease.Blocked) return new ApiResponses().ConflictResult(EarlierPaymentUnconfirmedMessage);
            }

            ListingWriteResult result;
            try
            {
                result = await _listings.Create(new ShopItemListing
                {
                    TenantId = TenantId,
                    ItemId = req.ItemId,
                    Title = title,
                    AskingPriceCents = req.AskingPriceCents,
                    ConditionGrade = grade!,
                    ConditionNotes = Blank(req.ConditionNotes),
                    Published = req.Published,
                    ListedByUserId = UserId,
                });
            }
            catch (Exception ex) when (IsListedUnitGuard(ex))
            {
                return new ApiResponses().ConflictResult(ListingRules.ListedUnitMessage);
            }
            return await Respond(result);
        }

        /// <summary>The per-listing lock on the unit's open row when that row is stranded with a hold
        /// on it (the only case Create has to release anything); null otherwise.</summary>
        private async Task<IAsyncDisposable?> LockStrandedHold(Guid itemId, string itemStatus)
        {
            if (!ListingRules.CanList(itemStatus)) return null;   // the create refuses anyway
            var open = await _listings.GetOpenForItem(itemId, TenantId);
            if (open is null || open.ItemStatus == ListingRules.ForSale || open.ReservedSaleId is null) return null;
            return await ListingLock(open.Id);
        }

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateShopListingRequest req)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (ListingRules.ValidatePrice(req.AskingPriceCents) is string priceError)
                return new ApiResponses().BadRequestResult(priceError);
            var grade = req.ConditionGrade?.Trim().ToLowerInvariant();
            if (!ListingRules.IsValidGrade(grade))
                return new ApiResponses().BadRequestResult("Condition grade must be excellent, good or fair.");

            var existing = await _listings.Get(id, TenantId);
            if (existing is null) return new ApiResponses().NotFoundResult("Listing not found.");
            var refusal = UpdateRefusal(existing);
            if (refusal is not null) return new ApiResponses().ConflictResult(refusal);

            var title = Blank(req.Title) ?? ListingRules.DefaultTitle(
                existing.ProductName, ListingRules.VariantLabel(existing.Size, existing.Color, existing.Gender), existing.UnitLabel);
            // The repository re-checks the same condition in its WHERE, so a sale or an unlist that
            // lands between the read above and this write still wins.
            var n = await _listings.Update(new ShopItemListing
            {
                Id = id,
                TenantId = TenantId,
                Title = title,
                AskingPriceCents = req.AskingPriceCents,
                ConditionGrade = grade!,
                ConditionNotes = Blank(req.ConditionNotes),
                Published = req.Published,
            });
            var after = await _listings.Get(id, TenantId);
            if (after is null) return new ApiResponses().NotFoundResult("Listing not found.");
            if (n == 0) return new ApiResponses().ConflictResult(UpdateRefusal(after) ?? "This listing changed; reload and try again.");
            return new ApiResponses().OkResult(ToDto(after, DateTime.UtcNow));
        }

        /// <summary>Null when the listing's details may be edited; otherwise the 409 copy.</summary>
        private static string? UpdateRefusal(ShopListingRow r)
        {
            var live = ListingRules.IsReservationLive(r.ReservedSaleId, r.ReservedAt, r.ReservedSaleStatus, DateTime.UtcNow);
            return ListingRules.State(r.UnlistedAt, r.ItemStatus, live) switch
            {
                "sold" => "This bike has been sold; the listing is the record of that sale and can't be edited.",
                "unlisted" => "This listing is closed. List the bike again to change its price or details.",
                _ => null,
            };
        }

        /// <summary>
        /// Take the unit off the floor, back to the status it was listed from (the fleet, the bench
        /// or retirement). 409 while an online checkout holds it or once it is sold; a stale hold is
        /// released first (PaymentIntent cancelled, sale abandoned, line detached).
        /// </summary>
        [HttpPost("{id:guid}/Unlist")]
        public async Task<IActionResult> Unlist(Guid id)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            await using var listingLock = await ListingLock(id);

            var row = await _listings.Get(id, TenantId);
            if (row is null) return new ApiResponses().NotFoundResult("Listing not found.");
            if (row.UnlistedAt is null && row.ItemStatus != "sold" && row.ReservedSaleId is not null)
            {
                if (row.ReservedSaleStatus == "paid") return new ApiResponses().ConflictResult(SoldOnlineMessage);
                if (ListingRules.IsReservationLive(row.ReservedSaleId, row.ReservedAt, row.ReservedSaleStatus, DateTime.UtcNow))
                    return new ApiResponses().ConflictResult("Someone is checking out this bike online right now. Try again in a few minutes.");
                var release = await _reservations.Release(row, HttpContext.RequestAborted);
                if (release == ReservationRelease.AlreadyPaid) return new ApiResponses().ConflictResult(SoldOnlineMessage);
                if (release == ReservationRelease.Blocked) return new ApiResponses().ConflictResult(PaymentInFlightMessage);
            }

            ListingWriteResult result;
            try
            {
                result = await _listings.Unlist(id, TenantId);
            }
            catch (Exception ex) when (IsListedUnitGuard(ex))
            {
                return new ApiResponses().ConflictResult(ListingRules.ListedUnitMessage);
            }
            return await Respond(result);
        }

        private async Task<IActionResult> Respond(ListingWriteResult result)
        {
            switch (result.Outcome)
            {
                case ListingWriteOutcome.NotFound:
                    return new ApiResponses().NotFoundResult(result.Error);
                case ListingWriteOutcome.Conflict when result.BlockingRentalStartsAt is DateTime startsAt:
                    return new ApiResponses().ConflictResult(
                        ListingRules.RentalBookedMessage(startsAt, _tenantContext.Tenant.Timezone));
                case ListingWriteOutcome.Conflict:
                    return new ApiResponses().ConflictResult(result.Error);
                default:
                    var row = await _listings.Get(result.Id!.Value, TenantId);
                    return row is null
                        ? new ApiResponses().NotFoundResult("Listing not found.")
                        : new ApiResponses().OkResult(ToDto(row, DateTime.UtcNow));
            }
        }

        // ── Photos ────────────────────────────────────────────────────────────────
        [HttpPost("{id:guid}/Photos")]
        [RequestSizeLimit(MaxBytes)]
        public async Task<IActionResult> AddPhoto(Guid id, IFormFile file, [FromForm] string? caption, CancellationToken ct)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var listing = await _listings.Get(id, TenantId);
            if (listing is null) return new ApiResponses().NotFoundResult("Listing not found.");

            if (file is null || file.Length == 0)
                return new ApiResponses().BadRequestResult("Pick a photo to upload.");
            if (file.Length > MaxBytes)
                return new ApiResponses().BadRequestResult("That photo is over the 5 MB limit. Try a smaller one.");
            if (!AllowedTypes.TryGetValue(file.ContentType, out var ext))
                return new ApiResponses().BadRequestResult($"Photos must be PNG, JPEG, or WebP (got {file.ContentType}).");

            // Cheap early-out before storing a file that cannot fit. The authoritative check is the
            // atomic count-and-insert under the listing row lock below; a file stored for an upload
            // that loses that race is deleted again.
            var limitMessage = $"That's the limit of {MaxPhotos} photos. Delete one to add another.";
            if (await _listings.CountPhotos(id, TenantId) >= MaxPhotos)
                return new ApiResponses().BadRequestResult(limitMessage);

            await using var stream = file.OpenReadStream();
            var url = await _imageStorage.SaveAsync(stream, TenantId, "shop-listing", ext, ct);
            (ShopListingPhoto? Photo, bool LimitReached) added;
            try
            {
                added = await _listings.AddPhoto(new ShopListingPhoto
                {
                    TenantId = TenantId,
                    ListingId = id,
                    ImageUrl = url,
                    Caption = Blank(caption),
                }, MaxPhotos);
            }
            catch
            {
                await SafeDeleteFile(url, ct);
                throw;
            }
            var saved = added.Photo;
            if (saved is null)
            {
                await SafeDeleteFile(url, ct);
                return added.LimitReached
                    ? new ApiResponses().BadRequestResult(limitMessage)
                    : new ApiResponses().NotFoundResult("Listing not found.");
            }
            return new ApiResponses().OkResult(new { id = saved.Id, url = saved.ImageUrl, caption = saved.Caption, sortOrder = saved.SortOrder });
        }

        [HttpDelete("{id:guid}/Photos/{photoId:guid}")]
        public async Task<IActionResult> DeletePhoto(Guid id, Guid photoId, CancellationToken ct)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var url = await _listings.DeletePhoto(photoId, id, TenantId);
            if (url is null) return new ApiResponses().NotFoundResult("Photo not found.");
            // The row is the record; a file left behind is untidy but harmless, so a storage
            // failure must not fail the request the user already sees as done.
            await SafeDeleteFile(url, ct);
            return new ApiResponses().OkResult();
        }

        [HttpPut("{id:guid}/Photos/Order")]
        public async Task<IActionResult> OrderPhotos(Guid id, [FromBody] ShopListingPhotoOrderRequest req)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var listing = await _listings.Get(id, TenantId);
            if (listing is null) return new ApiResponses().NotFoundResult("Listing not found.");
            await _listings.ReorderPhotos(id, TenantId, req.PhotoIds.Distinct().ToList());
            var photos = await _listings.ListPhotos(id, TenantId);
            return new ApiResponses().OkResult(new
            {
                photos = photos.Select(p => new { id = p.Id, url = p.ImageUrl, caption = p.Caption, sortOrder = p.SortOrder }),
            });
        }

        // ── Product condition ─────────────────────────────────────────────────────
        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPut("Products/{productId:guid}/Condition")]
        public async Task<IActionResult> SetProductCondition(Guid productId, [FromBody] SetShopProductConditionRequest req)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var condition = req.Condition?.Trim().ToLowerInvariant();
            if (!ListingRules.IsValidCondition(condition))
                return new ApiResponses().BadRequestResult("Condition must be 'new' or 'used'.");
            var n = await _listings.SetProductCondition(productId, TenantId, condition!);
            if (n == 0) return new ApiResponses().NotFoundResult("Product not found.");
            return new ApiResponses().OkResult(new { productId, condition });
        }

        // ── Shapes ────────────────────────────────────────────────────────────────
        /// <summary>ListingDto (docs: USED_SALES contract). Extra fields are additive.</summary>
        internal static object ToDto(ShopListingRow r, DateTime nowUtc)
        {
            var live = ListingRules.IsReservationLive(r.ReservedSaleId, r.ReservedAt, r.ReservedSaleStatus, nowUtc);
            var state = ListingRules.State(r.UnlistedAt, r.ItemStatus, live);
            var sold = state == "sold";
            var photos = r.Photos
                .Select(p => new { id = p.Id, url = p.ImageUrl, caption = p.Caption, sortOrder = p.SortOrder })
                .ToList();
            return new
            {
                id = r.Id,
                itemId = r.ItemId,
                title = r.Title,
                productId = r.ProductId,
                productName = r.ProductName,
                variantId = r.VariantId,
                variantLabel = ListingRules.VariantLabel(r.Size, r.Color, r.Gender),
                unitLabel = r.UnitLabel,
                serial = r.Serial,
                askingPriceCents = r.AskingPriceCents,
                conditionGrade = r.ConditionGrade,
                conditionNotes = r.ConditionNotes,
                published = r.Published,
                listedAtUtc = Utc(r.ListedAt),
                listedByName = r.ListedByName,
                unlistedAtUtc = UtcOrNull(r.UnlistedAt),
                state,
                soldAtUtc = sold ? UtcOrNull(r.SoldAt) : null,
                soldSaleId = sold ? r.SoldSaleId : null,
                soldOrderNumber = sold ? r.SoldOrderNumber : null,
                acquiredCostCents = r.AcquiredCostCents,
                photos,
                coverUrl = photos.FirstOrDefault()?.url ?? r.ProductImageUrl,
                // Additive: what the unit reads right now, when a live hold lapses, the product badge.
                unitStatus = r.ItemStatus,
                reservedUntilUtc = live && r.ReservedSaleStatus != "paid"
                    ? UtcOrNull(ListingRules.ReservationExpiresAtUtc(r.ReservedAt)) : null,
                productCondition = r.ProductCondition,
                // Where the unit goes if this listing comes down (available | maintenance | retired).
                priorStatus = ListingRules.RestoreStatusOnUnlist(r.PriorStatus),
            };
        }

        // SpecifyKind matters: the client parses these as UTC before converting to the tenant's
        // timezone, and a Kind-unspecified value serializes without the Z.
        internal static DateTime Utc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc);
        internal static DateTime? UtcOrNull(DateTime? d) => d is null ? null : Utc(d.Value);

        private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        private async Task SafeDeleteFile(string url, CancellationToken ct)
        {
            try { await _imageStorage.DeleteAsync(url, ct); }
            catch { /* best effort: the DB row is the source of truth */ }
        }
    }
}
