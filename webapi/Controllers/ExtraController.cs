using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Payments;
using Services.Repositories.Data.ExtrasData;
using Services.Repositories.Interfaces;
using Services.Storage;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Extras;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ExtraController : ControllerBase
    {
        private readonly IEventExtraRepository _extras;
        private readonly IEventRepository _events;
        private readonly IUserRepository _users;
        private readonly IWaiverRepository _waivers;
        private readonly IPaymentProvider _payments;
        private readonly IChargeRouter _chargeRouter;
        private readonly IMembershipRepository _memberships;
        private readonly IImageStorage _imageStorage;
        private readonly ITenantContext _tenantContext;

        public ExtraController(
            IEventExtraRepository extras,
            IEventRepository events,
            IUserRepository users,
            IWaiverRepository waivers,
            IPaymentProvider payments,
            IChargeRouter chargeRouter,
            IMembershipRepository memberships,
            IImageStorage imageStorage,
            ITenantContext tenantContext)
        {
            _extras = extras;
            _events = events;
            _users = users;
            _waivers = waivers;
            _payments = payments;
            _chargeRouter = chargeRouter;
            _memberships = memberships;
            _imageStorage = imageStorage;
            _tenantContext = tenantContext;
        }

        // ── Add-on check-in ───────────────────────────────────────────────────
        // The gate's scan flow can already check an add-on in, but only by scanning a QR that
        // belongs to the order. That is no use to whoever is working a campground with a tablet, and
        // a customer who bought ONLY an add-on has no ticket for the gate search to find them by.
        // This is the list-and-tick surface for those cases; it writes the same status the scan does.

        /// <summary>Add-on products this tenant sells, for the filter. Includes inactive ones,
        /// because last season's camping still has arrivals to record.</summary>
        [Authorize(Policy = TenantPermissions.Policy.SalesRedeem)]
        [HttpGet("CheckIn/Filters")]
        public async Task<IActionResult> CheckInFilters()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var products = await _extras.ListProducts(_tenantContext.TenantId, activeOnly: false);
            return new ApiResponses().OkResult(new ExtraCheckInFilters
            {
                Products = products
                    .OrderBy(p => p.SortOrder).ThenBy(p => p.Name)
                    .Select(p => new ExtraCheckInProductOption
                    {
                        Id = p.Id, Name = p.Name, Kind = p.Kind, IsActive = p.IsActive,
                    }).ToList(),
            });
        }

        /// <summary>
        /// Who bought an add-on in a window, and who has arrived. Defaults to a window around today
        /// rather than all history, so the page opens on the people actually turning up.
        /// </summary>
        [Authorize(Policy = TenantPermissions.Policy.SalesRedeem)]
        [HttpGet("CheckIn")]
        public async Task<IActionResult> CheckInList(
            [FromQuery] Guid? productId, [FromQuery] string? kind, [FromQuery] Guid? eventId,
            [FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? q,
            [FromQuery] string? arrival)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");

            // A name search is a deliberate "find this person wherever they are", so it ignores the
            // window. Someone standing at the gate whose camping is filed under next weekend must
            // still be findable.
            var searching = !string.IsNullOrWhiteSpace(q);
            // Dates arrive as plain yyyy-MM-dd and mean the TENANT'S days, not the server's. Parsing
            // them as local DateTimes would slide both edges by the server's offset, so a track in
            // Denver asking for "today" would get a window that ends at 6pm.
            var fromUtc = searching ? null : StartOfTenantDay(from);
            var toUtc = searching ? null : EndOfTenantDay(to);

            const int cap = 500;
            var rows = await _extras.SearchForCheckIn(
                _tenantContext.TenantId, productId, string.IsNullOrWhiteSpace(kind) ? null : kind, eventId,
                fromUtc, toUtc, q,
                arrivedOnly: arrival == "arrived", notArrivedOnly: arrival == "not_arrived",
                limit: cap);

            return new ApiResponses().OkResult(new ExtraCheckInResponse
            {
                Items = rows.Select(ToCheckInItem).ToList(),
                TotalCount = rows.Count,
                ArrivedCount = rows.Count(r => r.Status == "redeemed"),
                Truncated = rows.Count >= cap,
            });
        }

        /// <summary>
        /// Check an add-on in, or undo it. One toggle rather than two endpoints, matching the Event
        /// Riders check-in on the reports screen. SalesRedeem, so anyone working a gate or a
        /// campground can use it without catalog rights.
        /// </summary>
        [Authorize(Policy = TenantPermissions.Policy.SalesRedeem)]
        [HttpPut("CheckIn/{purchaseId:guid}")]
        public async Task<IActionResult> SetCheckIn(Guid purchaseId, [FromBody] SetExtraCheckInRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!Guid.TryParse(User.FindFirst("UserId")?.Value, out var staffId))
                return new ApiResponses().BadRequestResult("Invalid token.");

            var tenantId = _tenantContext.TenantId;
            if (req.CheckedIn)
            {
                // The 'paid' guard is in the SQL, so a cancelled or refunded add-on can't be checked
                // in. Reported rather than swallowed: silently doing nothing is how someone gets
                // waved through on a refunded camping spot.
                if (!await _extras.MarkRedeemed(purchaseId, tenantId, staffId, DateTime.UtcNow))
                {
                    return new ApiResponses().BadRequestResult(
                        "This add-on can't be checked in. It may be refunded, cancelled, or already checked in.");
                }
            }
            else if (!await _extras.UndoRedeemed(purchaseId, tenantId))
            {
                return new ApiResponses().BadRequestResult(
                    "This add-on isn't checked in, so there's nothing to undo.");
            }

            var fresh = await _extras.GetPurchaseWithProduct(purchaseId, tenantId);
            return fresh is null
                ? new ApiResponses().OkResult()
                : new ApiResponses().OkResult(new
                {
                    purchaseId,
                    status = fresh.Status,
                    arrived = fresh.Status == "redeemed",
                    arrivedAtUtc = fresh.RedeemedAtUtc.HasValue
                        ? DateTime.SpecifyKind(fresh.RedeemedAtUtc.Value, DateTimeKind.Utc)
                        : (DateTime?)null,
                });
        }

        private static ExtraCheckInItem ToCheckInItem(ExtraCheckInRow r) => new()
        {
            PurchaseId = r.Id,
            ProductName = r.ProductName,
            ProductKind = r.ProductKind,
            PurchaserName = r.PurchaserName ?? "",
            PurchaserEmail = r.PurchaserEmail ?? "",
            Quantity = r.Quantity,
            VariantLabel = BuildVariantLabel(r.SizeAtPurchase, r.ColorAtPurchase, r.GenderAtPurchase),
            AmountCents = r.AmountCents,
            Status = r.Status,
            Arrived = r.Status == "redeemed",
            ArrivedAtUtc = r.RedeemedAtUtc.HasValue
                ? DateTime.SpecifyKind(r.RedeemedAtUtc.Value, DateTimeKind.Utc) : null,
            ArrivedByName = r.RedeemedByName,
            EventId = r.EventId,
            EventTitle = r.EventTitle,
            EventStartsAtUtc = r.EventStartsAtUtc.HasValue
                ? DateTime.SpecifyKind(r.EventStartsAtUtc.Value, DateTimeKind.Utc) : null,
            PurchasedAtUtc = DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc),
        };

        private static string? BuildVariantLabel(string? size, string? color, string? gender)
        {
            var parts = new[] { size, color, gender }.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            return parts.Count == 0 ? null : string.Join(", ", parts);
        }

        private DateTime? StartOfTenantDay(string? date) => TenantDayBound(date, endOfDay: false);
        private DateTime? EndOfTenantDay(string? date) => TenantDayBound(date, endOfDay: true);

        /// <summary>
        /// A yyyy-MM-dd from the admin, read as a day in the TENANT'S timezone and returned as the
        /// matching UTC instant. Unparseable input yields null (no bound) rather than an error: this
        /// is a convenience filter, and refusing the whole list over a typo in a date box is a worse
        /// outcome than showing more rows than were asked for.
        /// </summary>
        private DateTime? TenantDayBound(string? date, bool endOfDay)
        {
            if (!DateOnly.TryParse(date, out var d)) return null;
            var local = endOfDay
                ? d.ToDateTime(new TimeOnly(23, 59, 59))
                : d.ToDateTime(TimeOnly.MinValue);
            var tzId = _tenantContext.Tenant?.Timezone;
            if (string.IsNullOrWhiteSpace(tzId)) return DateTime.SpecifyKind(local, DateTimeKind.Utc);
            try
            {
                return TimeZoneInfo.ConvertTimeToUtc(
                    DateTime.SpecifyKind(local, DateTimeKind.Unspecified),
                    TimeZoneInfo.FindSystemTimeZoneById(tzId));
            }
            catch (TimeZoneNotFoundException)
            {
                return DateTime.SpecifyKind(local, DateTimeKind.Utc);
            }
        }

        // ── Products: tenant admin CRUD ───────────────────────────────────────
        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpGet("Products/Admin")]
        public async Task<IActionResult> ListForAdmin()
        {
            var rows = await _extras.ListProducts(_tenantContext.TenantId, activeOnly: false);
            return new ApiResponses().OkResult(await HydrateProductsWithVariants(rows, activeOnly: false));
        }

        [HttpGet("Products")]
        public async Task<IActionResult> ListActive()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!_tenantContext.Tenant.ExtrasEnabled) return new ApiResponses().OkResult(new List<ExtraProductResponse>());
            var rows = await _extras.ListProducts(_tenantContext.TenantId, activeOnly: true);
            return new ApiResponses().OkResult(await HydrateProductsWithVariants(rows, activeOnly: true));
        }

        // Batched: pulls all variants for the product list in one query, sums sold-counts in
        // another, and stitches the response. Active-only filter hides inactive variants
        // from rider-facing views but keeps them in the admin list.
        private async Task<List<ExtraProductResponse>> HydrateProductsWithVariants(
            List<EventExtraProduct> products, bool activeOnly)
        {
            if (products.Count == 0) return new();
            var variantsByProduct = await _extras.ListVariantsForProducts(products.Select(p => p.Id));
            var allVariantIds = variantsByProduct.Values.SelectMany(v => v).Select(v => v.Id).ToList();
            var sold = await _extras.SumSoldVariants(allVariantIds);

            // Product-level sold count (across all variants + events) so admins can see
            // how close they are to the tenant-wide cap. One query per product is fine
            // here — admin list is small and this isn't on the rider hot path.
            var productSold = new Dictionary<Guid, int>();
            foreach (var p in products) productSold[p.Id] = await _extras.SumSoldProduct(p.Id);

            var responses = new List<ExtraProductResponse>();
            foreach (var p in products)
            {
                var resp = ToResponse(p, productSold.GetValueOrDefault(p.Id, 0));
                if (variantsByProduct.TryGetValue(p.Id, out var vlist))
                {
                    var filtered = activeOnly ? vlist.Where(v => v.IsActive) : vlist;
                    resp.Variants = filtered.Select(v => ToVariantResponse(v, sold.GetValueOrDefault(v.Id, 0))).ToList();
                }
                responses.Add(resp);
            }
            return responses;
        }

        // ── Variants ─────────────────────────────────────────────────────────
        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpGet("Products/{productId:guid}/Variants")]
        public async Task<IActionResult> ListVariants(Guid productId)
        {
            var product = await _extras.GetProduct(productId, _tenantContext.TenantId);
            if (product is null) return new ApiResponses().NotFoundResult("Add-on product not found.");
            var variants = await _extras.ListVariants(productId);
            var sold = await _extras.SumSoldVariants(variants.Select(v => v.Id));
            return new ApiResponses().OkResult(variants.Select(v => ToVariantResponse(v, sold.GetValueOrDefault(v.Id, 0))));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost("Products/{productId:guid}/Variants")]
        public async Task<IActionResult> CreateVariant(Guid productId, [FromBody] UpsertExtraVariantRequest req)
        {
            var product = await _extras.GetProduct(productId, _tenantContext.TenantId);
            if (product is null) return new ApiResponses().NotFoundResult("Add-on product not found.");
            var v = new EventExtraVariant
            {
                ProductId = productId,
                Size = NormaliseAttr(req.Size),
                Color = NormaliseAttr(req.Color),
                Gender = NormaliseAttr(req.Gender),
                Sku = string.IsNullOrWhiteSpace(req.Sku) ? null : req.Sku.Trim(),
                Tier = NormaliseAttr(req.Tier),
                Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
                PriceCents = req.PriceCents,
                Inventory = req.Inventory,
                ImageUrl = string.IsNullOrWhiteSpace(req.ImageUrl) ? null : req.ImageUrl.Trim(),
                SortOrder = req.SortOrder,
                IsActive = req.IsActive,
            };
            try { v.Id = await _extras.CreateVariant(v); }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
            {
                return new ApiResponses().BadRequestResult("A variant with the same size / color / gender already exists.");
            }
            return new ApiResponses().OkResult(ToVariantResponse(v, sold: 0));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPut("Products/{productId:guid}/Variants/{variantId:guid}")]
        public async Task<IActionResult> UpdateVariant(Guid productId, Guid variantId, [FromBody] UpsertExtraVariantRequest req)
        {
            var product = await _extras.GetProduct(productId, _tenantContext.TenantId);
            if (product is null) return new ApiResponses().NotFoundResult("Add-on product not found.");
            var existing = await _extras.GetVariant(variantId);
            if (existing is null || existing.ProductId != productId)
                return new ApiResponses().NotFoundResult("Variant not found.");
            existing.Size = NormaliseAttr(req.Size);
            existing.Color = NormaliseAttr(req.Color);
            existing.Gender = NormaliseAttr(req.Gender);
            existing.Sku = string.IsNullOrWhiteSpace(req.Sku) ? null : req.Sku.Trim();
            existing.Tier = NormaliseAttr(req.Tier);
            existing.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
            existing.PriceCents = req.PriceCents;
            existing.Inventory = req.Inventory;
            existing.ImageUrl = string.IsNullOrWhiteSpace(req.ImageUrl) ? null : req.ImageUrl.Trim();
            existing.SortOrder = req.SortOrder;
            existing.IsActive = req.IsActive;
            try { await _extras.UpdateVariant(existing); }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
            {
                return new ApiResponses().BadRequestResult("A variant with the same size / color / gender already exists.");
            }
            var sold = await _extras.SumSoldVariant(existing.Id);
            return new ApiResponses().OkResult(ToVariantResponse(existing, sold));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpDelete("Products/{productId:guid}/Variants/{variantId:guid}")]
        public async Task<IActionResult> DeleteVariant(Guid productId, Guid variantId)
        {
            var product = await _extras.GetProduct(productId, _tenantContext.TenantId);
            if (product is null) return new ApiResponses().NotFoundResult("Add-on product not found.");
            var existing = await _extras.GetVariant(variantId);
            if (existing is null || existing.ProductId != productId)
                return new ApiResponses().NotFoundResult("Variant not found.");
            try { await _extras.DeleteVariant(variantId); }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23503")
            {
                return new ApiResponses().BadRequestResult(
                    "This variant has purchases on file and can't be deleted. Set inactive instead.");
            }
            return new ApiResponses().OkResult();
        }

        // Single endpoint for both product images and variant images — the upload
        // returns a URL that the form stores on whichever record is being edited.
        // Same 5 MB / image-types contract as the EventController image endpoint.
        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost("Image")]
        [RequestSizeLimit(5 * 1024 * 1024)]
        public async Task<IActionResult> UploadImage(IFormFile file, CancellationToken ct)
        {
            if (file is null || file.Length == 0)
                return new ApiResponses().BadRequestResult("File is required.");
            if (file.Length > 5 * 1024 * 1024)
                return new ApiResponses().BadRequestResult("File exceeds 5 MB limit.");
            var allowed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["image/png"] = ".png",
                ["image/jpeg"] = ".jpg",
                ["image/webp"] = ".webp",
            };
            if (!allowed.TryGetValue(file.ContentType, out var ext))
                return new ApiResponses().BadRequestResult($"Unsupported content type: {file.ContentType}.");

            await using var stream = file.OpenReadStream();
            var url = await _imageStorage.SaveAsync(stream, _tenantContext.TenantId, "extra", ext, ct);
            return new ApiResponses().OkResult(new { imageUrl = url });
        }

        private static string? NormaliseAttr(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            return raw.Trim();
        }

        /// <summary>
        /// Resolves a buy-line into (variant?, unitPriceCents) and runs the inventory
        /// check. Returns Error with a friendly string when validation fails so the
        /// caller can short-circuit.
        ///
        /// Behaviour:
        /// - Product has any active variants → VariantId required, must belong to product,
        ///   must be active. Effective price = variant.PriceCents ?? product.PriceCents.
        ///   Inventory check is tenant-wide (variant.Inventory minus SumSoldVariant).
        /// - Product has no active variants → legacy single-SKU path. VariantId silently
        ///   ignored. Per-event eligibility.Inventory is the cap.
        /// </summary>
        internal record VariantResolveResult(EventExtraVariant? Variant, int UnitPriceCents, string? Error);
        private async Task<VariantResolveResult> ResolveVariantOrError(
            EventExtraProduct product, BuyExtrasItem item, EventExtraEligibility eligibility)
        {
            // Expiry: enforced before any inventory math so an expired product can't sneak through.
            if (product.ExpiresAt.HasValue && product.ExpiresAt.Value <= DateTime.UtcNow)
            {
                return new(null, 0, $"\"{product.Name}\" is no longer being sold.");
            }
            // Product-level (tenant-wide) inventory cap. Sums quantity across every event +
            // variant. Layered on top of the variant or per-event eligibility check below.
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
                        return new(null, 0, remaining <= 0
                            ? $"\"{qual}\" is sold out."
                            : $"Only {remaining} of \"{qual}\" left.");
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

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost("Products")]
        public async Task<IActionResult> Create([FromBody] UpsertExtraProductRequest req)
        {
            var p = new EventExtraProduct
            {
                TenantId = _tenantContext.TenantId,
                Name = req.Name.Trim(),
                Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
                ImageUrl = string.IsNullOrWhiteSpace(req.ImageUrl) ? null : req.ImageUrl.Trim(),
                Kind = req.Kind.Trim().ToLowerInvariant(),
                PriceCents = req.PriceCents,
                RiderPaidServiceChargeBps = req.RiderPaidServiceChargeBps,
                RequiresWaiver = req.RequiresWaiver,
                IsActive = req.IsActive,
                SortOrder = req.SortOrder,
                ExpiresAt = req.ExpiresAt?.ToUniversalTime(),
                Inventory = req.Inventory,
            };
            p.Id = await _extras.CreateProduct(p);
            return new ApiResponses().OkResult(ToResponse(p));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPut("Products/{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpsertExtraProductRequest req)
        {
            var existing = await _extras.GetProduct(id, _tenantContext.TenantId);
            if (existing is null) return new ApiResponses().NotFoundResult("Add-on product not found.");
            existing.Name = req.Name.Trim();
            existing.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
            existing.ImageUrl = string.IsNullOrWhiteSpace(req.ImageUrl) ? null : req.ImageUrl.Trim();
            existing.Kind = req.Kind.Trim().ToLowerInvariant();
            existing.PriceCents = req.PriceCents;
            existing.RiderPaidServiceChargeBps = req.RiderPaidServiceChargeBps;
            existing.RequiresWaiver = req.RequiresWaiver;
            existing.IsActive = req.IsActive;
            existing.SortOrder = req.SortOrder;
            existing.ExpiresAt = req.ExpiresAt?.ToUniversalTime();
            existing.Inventory = req.Inventory;
            await _extras.UpdateProduct(existing);
            var sold = await _extras.SumSoldProduct(existing.Id);
            return new ApiResponses().OkResult(ToResponse(existing, sold));
        }

        // Bulk-update sort_order after an admin drag-drops the catalog. The client
        // sends every visible row with its new position; we run a single UPDATE so
        // a partial failure can't leave the list half-reordered.
        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost("Products/Reorder")]
        public async Task<IActionResult> ReorderProducts([FromBody] ReorderExtraProductsRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (req.Items.Count == 0) return new ApiResponses().OkResult();
            var ids = req.Items.Select(i => i.Id).ToList();
            var orders = req.Items.Select(i => i.SortOrder).ToList();
            await _extras.UpdateProductSortOrders(_tenantContext.TenantId, ids, orders);
            return new ApiResponses().OkResult();
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpDelete("Products/{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            try { await _extras.DeleteProduct(id, _tenantContext.TenantId); }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23503")
            {
                return new ApiResponses().BadRequestResult(
                    "This add-on has purchases on file and can't be deleted. Set inactive instead.");
            }
            return new ApiResponses().OkResult();
        }

        // ── Rider purchase (standalone, attached to an event) ────────────────
        [Authorize]
        [HttpPost("Buy")]
        public async Task<IActionResult> Buy([FromBody] BuyExtrasRequest req, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!_tenantContext.Tenant.ExtrasEnabled) return new ApiResponses().BadRequestResult("This tenant doesn't sell add-ons.");
            if (!Guid.TryParse(User.FindFirst("UserId")?.Value, out var userId))
                return new ApiResponses().BadRequestResult("Invalid token.");

            var user = await _users.GetById(userId);
            if (user is null) return new ApiResponses().BadRequestResult("User not found.");

            var ev = await _events.GetById(req.EventId, _tenantContext.TenantId);
            if (ev is null || ev.Status != "scheduled" || ev.EndsAt < DateTime.UtcNow)
                return new ApiResponses().BadRequestResult("Event not available.");

            // Dedupe by (product, variant), sum quantities. A null VariantId is its own bucket.
            var items = req.Items
                .Where(i => i.Quantity > 0)
                .GroupBy(i => new { i.ProductId, i.VariantId })
                .Select(g => new BuyExtrasItem
                {
                    ProductId = g.Key.ProductId,
                    VariantId = g.Key.VariantId,
                    Quantity = g.Sum(x => x.Quantity),
                })
                .ToList();
            if (items.Count == 0) return new ApiResponses().BadRequestResult("Cart is empty.");

            // Eligibility + capacity check + waiver gate, while computing the line totals.
            var tenant = _tenantContext.Tenant;
            var totalAmountCents = 0;
            var totalServiceChargeCents = 0;
            Guid? signatureId = null;
            var lines = new List<(EventExtraProduct Product, EventExtraVariant? Variant, int Quantity,
                                  int UnitAmount, int UnitServiceCharge, int UnitPriceFrozen)>();
            foreach (var item in items)
            {
                var product = await _extras.GetProduct(item.ProductId, _tenantContext.TenantId);
                if (product is null || !product.IsActive)
                    return new ApiResponses().BadRequestResult("One of the selected add-ons isn't available.");

                var elig = await _extras.GetEligibility(req.EventId, product.Id);
                if (elig is null)
                    return new ApiResponses().BadRequestResult($"\"{product.Name}\" isn't offered at this event.");

                // Variants short-circuit the per-event inventory cap — variants carry tenant-wide stock.
                var resolved = await ResolveVariantOrError(product, item, elig);
                if (resolved.Error is not null) return new ApiResponses().BadRequestResult(resolved.Error);

                if (product.RequiresWaiver && signatureId is null)
                {
                    var activeWaiver = await _waivers.GetActive(_tenantContext.TenantId);
                    if (activeWaiver is not null)
                    {
                        var sig = await _waivers.GetSignature(userId, activeWaiver.Id);
                        if (sig is null)
                            return new ApiResponses().BadRequestResult(
                                $"\"{product.Name}\" requires a signed waiver. Sign it on your profile first.");
                        signatureId = sig.Id;
                    }
                }

                var unitPriceFrozen = resolved.UnitPriceCents;
                var serviceChargePerUnit = (int)((long)unitPriceFrozen * tenant.ServiceChargeBps / 10_000L);
                var riderPortionPerUnit = (int)((long)serviceChargePerUnit * product.RiderPaidServiceChargeBps / 10_000L);
                var unitAmount = unitPriceFrozen + riderPortionPerUnit;

                lines.Add((product, resolved.Variant, item.Quantity, unitAmount, serviceChargePerUnit, unitPriceFrozen));
                totalAmountCents += unitAmount * item.Quantity;
                totalServiceChargeCents += serviceChargePerUnit * item.Quantity;
            }

            // Create one purchase row per unit so each gets its own QR. Same pattern
            // as event-ticket carts. Variant attrs frozen per row.
            var purchaseIds = new List<Guid>();
            foreach (var line in lines)
            {
                for (int q = 0; q < line.Quantity; q++)
                {
                    var p = new EventExtraPurchase
                    {
                        TenantId = _tenantContext.TenantId,
                        EventId = req.EventId,
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
                    var created = await _extras.CreatePurchase(p);
                    purchaseIds.Add(created.Id);
                }
            }

            // Single PI for the whole cart. The webhook flips each row to 'paid' on success.
            var metadata = new Dictionary<string, string>
            {
                ["tenant_id"] = _tenantContext.TenantId.ToString(),
                ["sale_kind"] = "event_extra",
                ["event_id"] = req.EventId.ToString(),
                ["user_id"] = userId.ToString(),
                ["extra_purchase_ids"] = string.Join(",", purchaseIds),
            };
            // Direct-charge tenants charge on their own connected account; our service fee rides as
            // the Stripe application fee.
            PaymentIntentCreated intent;
            ChargePlan chargePlan;
            try
            {
                chargePlan = _chargeRouter.Plan(tenant, totalServiceChargeCents, totalAmountCents);
                intent = await _payments.CreatePaymentIntentAsync(totalAmountCents, "usd", metadata, user.Email,
                    connectedAccountId: chargePlan.ConnectedAccountId,
                    applicationFeeCents: chargePlan.ApplicationFeeCents, ct: ct);
            }
            catch (InvalidOperationException ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }

            foreach (var id in purchaseIds)
            {
                await _extras.SetPaymentIntentId(id, intent.IntentId);
                if (chargePlan.IsDirect)
                {
                    await _extras.MarkDirectCharge(id, tenant.Id, chargePlan.ConnectedAccountId!);
                }
            }

            return new ApiResponses().OkResult(new BuyExtrasResponse
            {
                PurchaseIds = purchaseIds,
                ClientSecret = intent.ClientSecret,
                AmountCents = totalAmountCents,
                RiderServiceChargeCents = totalServiceChargeCents,
            });
        }

        [Authorize]
        [HttpGet("Mine")]
        public async Task<IActionResult> ListMine()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!Guid.TryParse(User.FindFirst("UserId")?.Value, out var userId))
                return new ApiResponses().BadRequestResult("Invalid token.");

            var rows = await _extras.ListMine(userId, _tenantContext.TenantId);
            var products = (await _extras.ListProducts(_tenantContext.TenantId, activeOnly: false))
                .ToDictionary(p => p.Id);
            // Hydrate event titles per row. N is small in practice.
            var responses = new List<MyExtraResponse>();
            foreach (var r in rows)
            {
                var ev = r.EventId.HasValue
                    ? await _events.GetById(r.EventId.Value, _tenantContext.TenantId)
                    : null;
                // Untethered counter-merch rows have no event — keep them in the list
                // with null event fields so the rider still sees their purchase.
                if (r.EventId.HasValue && ev is null) continue;
                var product = products.GetValueOrDefault(r.ProductId);
                responses.Add(new MyExtraResponse
                {
                    Id = r.Id,
                    RedemptionToken = r.RedemptionToken,
                    EventId = r.EventId,
                    EventTitle = ev?.Title,
                    EventStartsAtUtc = ev != null ? DateTime.SpecifyKind(ev.StartsAt, DateTimeKind.Utc) : null,
                    ProductName = product?.Name ?? "Add-on",
                    Kind = product?.Kind ?? "other",
                    Quantity = r.Quantity,
                    AmountCents = r.AmountCents,
                    Status = r.Status,
                    CreatedAtUtc = DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc),
                });
            }
            return new ApiResponses().OkResult(responses);
        }

        private static ExtraProductResponse ToResponse(EventExtraProduct p, int sold = 0) => new()
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description,
            ImageUrl = p.ImageUrl,
            Kind = p.Kind,
            PriceCents = p.PriceCents,
            RiderPaidServiceChargeBps = p.RiderPaidServiceChargeBps,
            RequiresWaiver = p.RequiresWaiver,
            IsActive = p.IsActive,
            SortOrder = p.SortOrder,
            ExpiresAt = p.ExpiresAt.HasValue
                ? DateTime.SpecifyKind(p.ExpiresAt.Value, DateTimeKind.Utc)
                : null,
            Inventory = p.Inventory,
            Sold = sold,
            Remaining = p.Inventory.HasValue ? Math.Max(0, p.Inventory.Value - sold) : -1,
            Variants = new(),     // hydrated by HydrateProductsWithVariants when needed
        };

        private static ExtraVariantResponse ToVariantResponse(EventExtraVariant v, int sold) => new()
        {
            Id = v.Id,
            ProductId = v.ProductId,
            Size = v.Size,
            Color = v.Color,
            Gender = v.Gender,
            Sku = v.Sku,
            Tier = v.Tier,
            Description = v.Description,
            PriceCents = v.PriceCents,
            Inventory = v.Inventory,
            Sold = sold,
            Remaining = v.Inventory.HasValue ? Math.Max(0, v.Inventory.Value - sold) : -1,
            ImageUrl = v.ImageUrl,
            SortOrder = v.SortOrder,
            IsActive = v.IsActive,
        };
    }
}
