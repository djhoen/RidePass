using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Services.Helpers;
using Services.Repositories.Data.BikeShopData;
using Services.Repositories.Interfaces;
using Services.Storage;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.BikeShop;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    // Bike shop admin: catalog + inventory + purchasing management. All CatalogManage, tenant-scoped.
    // The shop's own catalog is fully isolated from concessions and event extras by construction
    // (docs/bike-shop.md) — nothing here reads or writes those tables.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
    public class BikeShopController : ControllerBase
    {
        private readonly IBikeShopRepository _shop;
        private readonly ITenantContext _tenantContext;
        private readonly Services.Helpers.ISmtpEmailer _emailer;
        private readonly IImageStorage _imageStorage;

        public BikeShopController(IBikeShopRepository shop, ITenantContext tenantContext,
            Services.Helpers.ISmtpEmailer emailer, IImageStorage imageStorage)
        {
            _shop = shop;
            _tenantContext = tenantContext;
            _emailer = emailer;
            _imageStorage = imageStorage;
        }

        private Guid TenantId => _tenantContext.TenantId;
        private bool NoTenant => !_tenantContext.IsResolved;
        private Guid? UserId => Guid.TryParse(User.FindFirst("UserId")?.Value, out var id) ? id : null;

        // ── Categories ────────────────────────────────────────────────────────────
        [HttpGet("Categories")]
        public async Task<IActionResult> ListCategories([FromQuery] bool activeOnly = false)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            return new ApiResponses().OkResult(await _shop.ListCategories(TenantId, activeOnly));
        }

        [HttpPost("Categories")]
        public async Task<IActionResult> CreateCategory([FromBody] UpsertShopCategoryRequest req)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var id = await _shop.CreateCategory(new ShopCategory
            {
                TenantId = TenantId, Name = req.Name.Trim(), ParentId = req.ParentId,
                SortOrder = req.SortOrder, IsActive = req.IsActive,
            });
            return new ApiResponses().OkResult(new { id });
        }

        [HttpPut("Categories/{id:guid}")]
        public async Task<IActionResult> UpdateCategory(Guid id, [FromBody] UpsertShopCategoryRequest req)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var n = await _shop.UpdateCategory(new ShopCategory
            {
                Id = id, TenantId = TenantId, Name = req.Name.Trim(), ParentId = req.ParentId,
                SortOrder = req.SortOrder, IsActive = req.IsActive,
            });
            return n == 0 ? new ApiResponses().NotFoundResult("Category not found.") : new ApiResponses().OkResult();
        }

        [HttpDelete("Categories/{id:guid}")]
        public async Task<IActionResult> DeleteCategory(Guid id)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var n = await _shop.DeleteCategory(id, TenantId);
            return n == 0 ? new ApiResponses().NotFoundResult("Category not found.") : new ApiResponses().OkResult();
        }

        // ── Suppliers ─────────────────────────────────────────────────────────────
        [HttpGet("Suppliers")]
        public async Task<IActionResult> ListSuppliers([FromQuery] bool activeOnly = false)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            return new ApiResponses().OkResult(await _shop.ListSuppliers(TenantId, activeOnly));
        }

        [HttpPost("Suppliers")]
        public async Task<IActionResult> CreateSupplier([FromBody] UpsertShopSupplierRequest req)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var id = await _shop.CreateSupplier(new ShopSupplier
            {
                TenantId = TenantId, Name = req.Name.Trim(), ContactName = req.ContactName?.Trim(),
                Email = req.Email?.Trim(), Phone = req.Phone?.Trim(), Notes = req.Notes?.Trim(),
                IsActive = req.IsActive,
            });
            return new ApiResponses().OkResult(new { id });
        }

        [HttpPut("Suppliers/{id:guid}")]
        public async Task<IActionResult> UpdateSupplier(Guid id, [FromBody] UpsertShopSupplierRequest req)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var n = await _shop.UpdateSupplier(new ShopSupplier
            {
                Id = id, TenantId = TenantId, Name = req.Name.Trim(), ContactName = req.ContactName?.Trim(),
                Email = req.Email?.Trim(), Phone = req.Phone?.Trim(), Notes = req.Notes?.Trim(),
                IsActive = req.IsActive,
            });
            return n == 0 ? new ApiResponses().NotFoundResult("Supplier not found.") : new ApiResponses().OkResult();
        }

        // ── Job templates ─────────────────────────────────────────────────────────
        // Saved standard jobs. Managing the library is a catalog act (this controller's policy);
        // APPLYING one to a work order is counter work and lives on the work order controller.

        [HttpGet("JobTemplates")]
        public async Task<IActionResult> ListJobTemplates([FromQuery] bool activeOnly = false)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            return new ApiResponses().OkResult(await _shop.ListJobTemplates(TenantId, activeOnly));
        }

        [HttpPost("JobTemplates")]
        public async Task<IActionResult> SaveJobTemplate([FromBody] UpsertJobTemplateRequest req)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (string.IsNullOrWhiteSpace(req.Name))
                return new ApiResponses().BadRequestResult("Give the job a name.");

            // Validate line shapes here so a bad template can't be saved and then fail later on
            // the work order, where the counter has no idea why.
            foreach (var l in req.Lines)
            {
                if (l.LineKind == "labor" && string.IsNullOrWhiteSpace(l.Description))
                    return new ApiResponses().BadRequestResult("Every labor line needs a description.");
                if (l.LineKind == "part" && l.VariantId is null)
                    return new ApiResponses().BadRequestResult("Every part line needs a product picked.");
                if (l.Quantity < 1)
                    return new ApiResponses().BadRequestResult("Quantities must be at least 1.");
            }

            try
            {
                var id = await _shop.SaveJobTemplate(new ShopJobTemplate
                {
                    Id = req.Id ?? Guid.Empty,
                    TenantId = TenantId,
                    Name = req.Name.Trim(),
                    FitsNote = string.IsNullOrWhiteSpace(req.FitsNote) ? null : req.FitsNote.Trim(),
                    Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim(),
                    IsActive = req.IsActive,
                    SortOrder = req.SortOrder,
                }, req.Lines.Select(l => new ShopJobTemplateLine
                {
                    LineKind = l.LineKind,
                    Description = string.IsNullOrWhiteSpace(l.Description) ? null : l.Description.Trim(),
                    VariantId = l.LineKind == "part" ? l.VariantId : null,
                    Quantity = l.Quantity,
                    UnitPriceCents = l.UnitPriceCents,
                    EstimatedMinutes = l.LineKind == "labor" ? l.EstimatedMinutes : null,
                }));
                return new ApiResponses().OkResult(new { id });
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
            {
                return new ApiResponses().BadRequestResult("A job with that name already exists.");
            }
        }

        [HttpDelete("JobTemplates/{id:guid}")]
        public async Task<IActionResult> DeleteJobTemplate(Guid id)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var n = await _shop.DeleteJobTemplate(id, TenantId);
            return n == 0 ? new ApiResponses().NotFoundResult("Job not found.") : new ApiResponses().OkResult();
        }

        // ── Agreements ────────────────────────────────────────────────────────────
        // The rental agreement / repair authorization text customers sign. CatalogManage (this
        // controller's policy) because writing the terms is a management act, while CAPTURING a
        // signature is counter work and lives on BikeShopPhotoController under ShopCounter.

        [HttpGet("Agreements/{kind}")]
        public async Task<IActionResult> GetAgreement(string kind)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (kind is not ("rental_agreement" or "work_order_terms"))
                return new ApiResponses().BadRequestResult("Unknown agreement type.");
            return new ApiResponses().OkResult(await _shop.GetActiveAgreement(TenantId, kind));
        }

        /// <summary>Publishes new terms as a NEW VERSION rather than editing in place, so every
        /// existing signature keeps proving what that customer actually agreed to. Renters are
        /// re-asked to sign at their next checkout, which is the intended consequence.</summary>
        [HttpPost("Agreements/{kind}")]
        public async Task<IActionResult> PublishAgreement(string kind, [FromBody] PublishShopAgreementRequest req)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (kind is not ("rental_agreement" or "work_order_terms"))
                return new ApiResponses().BadRequestResult("Unknown agreement type.");
            if (string.IsNullOrWhiteSpace(req.Title))
                return new ApiResponses().BadRequestResult("Give the agreement a title.");
            if (string.IsNullOrWhiteSpace(req.Body))
                return new ApiResponses().BadRequestResult("The agreement text can't be empty.");

            var id = await _shop.PublishAgreement(TenantId, kind, req.Title.Trim(), req.Body.Trim());
            return new ApiResponses().OkResult(new { id });
        }

        // ── Products ──────────────────────────────────────────────────────────────
        [HttpGet("Products")]
        public async Task<IActionResult> ListProducts([FromQuery] bool activeOnly = false)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            return new ApiResponses().OkResult(await _shop.ListProducts(TenantId, activeOnly));
        }

        // Paged + searchable catalog for the admin list screen. The unpaged endpoint above stays
        // for callers that genuinely need the whole catalog in memory (the register, CSV import
        // de-duplication, the storefront), which must not silently start seeing one page.
        //
        // sellable/rentable are nullable so one endpoint serves both lists: the retail catalog asks
        // for sellable, the rental fleet asks for rentable, and a product flagged both appears in
        // each. Omitting them returns everything.
        [HttpGet("Products/Page")]
        public async Task<IActionResult> SearchProducts(
            [FromQuery] string? search = null,
            [FromQuery] Guid? categoryId = null,
            [FromQuery] Guid? supplierId = null,
            [FromQuery] bool activeOnly = false,
            [FromQuery] bool? sellable = null,
            [FromQuery] bool? rentable = null,
            [FromQuery] bool lowStockOnly = false,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 25)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var page_ = await _shop.SearchProducts(TenantId, new ShopProductQuery
            {
                Search = search,
                CategoryId = categoryId,
                SupplierId = supplierId,
                ActiveOnly = activeOnly,
                Sellable = sellable,
                Rentable = rentable,
                LowStockOnly = lowStockOnly,
                Page = page,
                PageSize = pageSize,
            });
            return new ApiResponses().OkResult(new { rows = page_.Rows, total = page_.Total, totals = page_.Totals });
        }

        [HttpGet("Products/{id:guid}")]
        public async Task<IActionResult> GetProduct(Guid id)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var p = await _shop.GetProduct(id, TenantId);
            return p is null ? new ApiResponses().NotFoundResult("Product not found.") : new ApiResponses().OkResult(p);
        }

        [HttpPost("Products")]
        public async Task<IActionResult> CreateProduct([FromBody] UpsertShopProductRequest req)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!req.IsSellable && !req.IsRentable)
                return new ApiResponses().BadRequestResult("A product must be sellable, rentable, or both.");
            var id = await _shop.CreateProduct(MapProduct(new ShopProduct { TenantId = TenantId }, req));
            return new ApiResponses().OkResult(new { id });
        }

        [HttpPut("Products/{id:guid}")]
        public async Task<IActionResult> UpdateProduct(Guid id, [FromBody] UpsertShopProductRequest req)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!req.IsSellable && !req.IsRentable)
                return new ApiResponses().BadRequestResult("A product must be sellable, rentable, or both.");
            var n = await _shop.UpdateProduct(MapProduct(new ShopProduct { Id = id, TenantId = TenantId }, req));
            return n == 0 ? new ApiResponses().NotFoundResult("Product not found.") : new ApiResponses().OkResult();
        }

        private static ShopProduct MapProduct(ShopProduct p, UpsertShopProductRequest req)
        {
            p.Name = req.Name.Trim();
            p.Description = req.Description?.Trim();
            p.Brand = req.Brand?.Trim();
            p.ImageUrl = req.ImageUrl?.Trim();
            p.CategoryId = req.CategoryId;
            p.SupplierId = req.SupplierId;
            p.IsSellable = req.IsSellable;
            p.IsPublished = req.IsPublished;
            p.IsRentable = req.IsRentable;
            p.IsActive = req.IsActive;
            p.SortOrder = req.SortOrder;
            return p;
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost("Image")]
        [RequestSizeLimit(5 * 1024 * 1024)]
        public async Task<IActionResult> UploadImage(IFormFile file, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (file is null || file.Length == 0) return new ApiResponses().BadRequestResult("File is required.");
            if (file.Length > 5 * 1024 * 1024) return new ApiResponses().BadRequestResult("File exceeds 5 MB limit.");
            var allowed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["image/png"] = ".png",
                ["image/jpeg"] = ".jpg",
                ["image/webp"] = ".webp",
            };
            if (!allowed.TryGetValue(file.ContentType, out var ext))
                return new ApiResponses().BadRequestResult($"Unsupported content type: {file.ContentType}.");
            await using var stream = file.OpenReadStream();
            var url = await _imageStorage.SaveAsync(stream, TenantId, "shop", ext, ct);
            return new ApiResponses().OkResult(new { imageUrl = url });
        }

        // ── Variants ──────────────────────────────────────────────────────────────
        [HttpPost("Products/{productId:guid}/Variants")]
        public async Task<IActionResult> CreateVariant(Guid productId, [FromBody] UpsertShopVariantRequest req)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            // Scope check: the product must be in this tenant before a variant attaches to it.
            if (await _shop.GetProduct(productId, TenantId) is null)
                return new ApiResponses().NotFoundResult("Product not found.");

            var v = MapVariant(new ShopVariant { TenantId = TenantId, ProductId = productId }, req);
            v.TrackingKind = req.TrackingKind;   // fixed at creation only
            return await GuardUnique(async () =>
            {
                var id = await _shop.CreateVariant(v);
                return new ApiResponses().OkResult(new { id });
            });
        }

        [HttpPut("Variants/{id:guid}")]
        public async Task<IActionResult> UpdateVariant(Guid id, [FromBody] UpsertShopVariantRequest req)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var v = MapVariant(new ShopVariant { Id = id, TenantId = TenantId }, req);
            return await GuardUnique(async () =>
            {
                var n = await _shop.UpdateVariant(v);
                return n == 0 ? new ApiResponses().NotFoundResult("Variant not found.") : new ApiResponses().OkResult();
            });
        }

        private static ShopVariant MapVariant(ShopVariant v, UpsertShopVariantRequest req)
        {
            v.Sku = Blank(req.Sku);
            v.Barcode = Blank(req.Barcode);
            v.Size = Blank(req.Size);
            v.Color = Blank(req.Color);
            v.Gender = Blank(req.Gender);
            v.SalePriceCents = req.SalePriceCents;
            v.MsrpCents = req.MsrpCents;
            v.DailyRateCents = req.DailyRateCents;
            v.DepositCents = req.DepositCents;
            v.CostCents = req.CostCents;
            v.Mpn = Blank(req.Mpn);
            v.LowStockThreshold = req.LowStockThreshold;
            v.ReorderPoint = req.ReorderPoint;
            v.ReorderLevel = req.ReorderLevel;
            v.VendorPartNumber = Blank(req.VendorPartNumber);
            v.IsActive = req.IsActive;
            return v;
        }

        // ── Serialized items ──────────────────────────────────────────────────────
        [HttpGet("Variants/{variantId:guid}/Items")]
        public async Task<IActionResult> ListItems(Guid variantId)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            return new ApiResponses().OkResult(await _shop.ListItems(variantId, TenantId));
        }

        [HttpPost("Variants/{variantId:guid}/Items")]
        public async Task<IActionResult> CreateItem(Guid variantId, [FromBody] UpsertShopItemRequest req)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var variant = await _shop.GetVariant(variantId, TenantId);
            if (variant is null) return new ApiResponses().NotFoundResult("Variant not found.");
            if (variant.TrackingKind != "serialized")
                return new ApiResponses().BadRequestResult("Only serialized variants have individually tracked units. Use stock adjustment for pool variants.");

            return await GuardUnique(async () =>
            {
                var id = await _shop.CreateItem(new ShopItem
                {
                    TenantId = TenantId, VariantId = variantId, Label = req.Label.Trim(),
                    Serial = Blank(req.Serial), Notes = Blank(req.Notes),
                    Status = "available", AcquiredCostCents = req.AcquiredCostCents,
                });
                return new ApiResponses().OkResult(new { id });
            });
        }

        [HttpPut("Items/{id:guid}")]
        public async Task<IActionResult> UpdateItem(Guid id, [FromBody] UpsertShopItemRequest req)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            // Status here only moves a unit among the admin-settable states; 'sold' / 'rented_out'
            // are owned by transactions and rejected by the DTO regex.
            var status = req.Status ?? "available";
            return await GuardUnique(async () =>
            {
                var n = await _shop.UpdateItem(new ShopItem
                {
                    Id = id, TenantId = TenantId, Label = req.Label.Trim(), Serial = Blank(req.Serial),
                    Notes = Blank(req.Notes), Status = status, AcquiredCostCents = req.AcquiredCostCents,
                });
                return n == 0 ? new ApiResponses().NotFoundResult("Item not found.") : new ApiResponses().OkResult();
            });
        }

        // ── Stock ─────────────────────────────────────────────────────────────────
        [HttpPost("Variants/{variantId:guid}/AdjustStock")]
        public async Task<IActionResult> AdjustStock(Guid variantId, [FromBody] AdjustStockRequest req)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (req.Delta == 0) return new ApiResponses().BadRequestResult("Enter a non-zero adjustment.");

            var variant = await _shop.GetVariant(variantId, TenantId);
            if (variant is null) return new ApiResponses().NotFoundResult("Variant not found.");
            if (variant.TrackingKind != "pool")
                return new ApiResponses().BadRequestResult("Serialized variants are tracked per unit — add or retire items instead of adjusting a count.");

            var newQty = await _shop.AdjustPoolStock(variantId, TenantId, req.Delta, "adjustment",
                req.Note.Trim(), UserId);
            return newQty is null
                ? new ApiResponses().BadRequestResult("That adjustment would drive stock below zero.")
                : new ApiResponses().OkResult(new { stockOnHand = newQty.Value });
        }

        [HttpGet("Variants/{variantId:guid}/Movements")]
        public async Task<IActionResult> ListMovements(Guid variantId, [FromQuery] int limit = 100)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            return new ApiResponses().OkResult(await _shop.ListMovements(variantId, TenantId, Math.Clamp(limit, 1, 500)));
        }

        // ── Purchase orders ───────────────────────────────────────────────────────
        [HttpGet("PurchaseOrders")]
        public async Task<IActionResult> ListPurchaseOrders()
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            return new ApiResponses().OkResult(await _shop.ListPurchaseOrders(TenantId));
        }

        [HttpGet("PurchaseOrders/{id:guid}")]
        public async Task<IActionResult> GetPurchaseOrder(Guid id)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var po = await _shop.GetPurchaseOrder(id, TenantId);
            return po is null ? new ApiResponses().NotFoundResult("Purchase order not found.") : new ApiResponses().OkResult(po);
        }

        [HttpPost("PurchaseOrders")]
        public async Task<IActionResult> CreatePurchaseOrder([FromBody] UpsertPurchaseOrderRequest req)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var id = await _shop.CreatePurchaseOrder(new ShopPurchaseOrder
            {
                TenantId = TenantId, SupplierId = req.SupplierId, Reference = Blank(req.Reference),
                Notes = Blank(req.Notes), ExpectedAt = req.ExpectedAt, Status = "open",
                CreatedByUserId = UserId,
            });
            return new ApiResponses().OkResult(new { id });
        }

        /// <summary>Everything sitting at or below its reorder point, ready to turn into POs.</summary>
        [HttpGet("Reorder")]
        public async Task<IActionResult> ReorderWorklist()
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            return new ApiResponses().OkResult(await _shop.GetReorderWorklist(TenantId));
        }

        /// <summary>Raise a purchase order from picked reorder rows (one supplier per PO).</summary>
        [HttpPost("Reorder/PurchaseOrder")]
        public async Task<IActionResult> CreateReorderPo([FromBody] CreateReorderPoRequest req)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (req.Lines is null || req.Lines.Count == 0)
                return new ApiResponses().BadRequestResult("Pick at least one item to order.");
            if (req.Lines.Any(l => l.QuantityOrdered < 1))
                return new ApiResponses().BadRequestResult("Every ordered quantity must be at least 1.");

            var lines = req.Lines
                .Select(l => (l.VariantId, l.QuantityOrdered, l.UnitCostCents))
                .ToList();
            var poId = await _shop.CreatePurchaseOrderWithLines(
                TenantId, req.SupplierId, Blank(req.Reference), req.ExpectedAt, UserId, lines);
            return poId is null
                ? new ApiResponses().BadRequestResult("Could not create the purchase order. Check the supplier and items.")
                : new ApiResponses().OkResult(new { id = poId });
        }

        [HttpPut("PurchaseOrders/{id:guid}")]
        public async Task<IActionResult> UpdatePurchaseOrder(Guid id, [FromBody] UpsertPurchaseOrderRequest req)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var po = await _shop.GetPurchaseOrder(id, TenantId);
            if (po is null) return new ApiResponses().NotFoundResult("Purchase order not found.");
            // Editing details is only meaningful while the PO is still open (not yet receiving).
            if (po.Status is "received" or "cancelled")
                return new ApiResponses().BadRequestResult("This purchase order is closed and can't be edited.");
            po.SupplierId = req.SupplierId;
            po.Reference = Blank(req.Reference);
            po.Notes = Blank(req.Notes);
            po.ExpectedAt = req.ExpectedAt;
            await _shop.UpdatePurchaseOrder(po);
            return new ApiResponses().OkResult();
        }

        [HttpPost("PurchaseOrders/{id:guid}/Lines")]
        public async Task<IActionResult> AddLine(Guid id, [FromBody] AddPurchaseOrderLineRequest req)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var po = await _shop.GetPurchaseOrder(id, TenantId);
            if (po is null) return new ApiResponses().NotFoundResult("Purchase order not found.");
            if (po.Status is "received" or "cancelled")
                return new ApiResponses().BadRequestResult("This purchase order is closed.");
            if (await _shop.GetVariant(req.VariantId, TenantId) is null)
                return new ApiResponses().BadRequestResult("That variant doesn't exist at this shop.");

            var lineId = await _shop.AddPurchaseOrderLine(new ShopPoLine
            {
                PoId = id, VariantId = req.VariantId,
                QuantityOrdered = req.QuantityOrdered, UnitCostCents = req.UnitCostCents,
            }, TenantId);
            return lineId == Guid.Empty
                ? new ApiResponses().BadRequestResult("Could not add the line.")
                : new ApiResponses().OkResult(new { id = lineId });
        }

        // ── CSV import + variant matrix ───────────────────────────────────────────

        // Two-pass import: the client posts the same CSV with dryRun=true for a validated
        // preview (counts + row-level errors), then dryRun=false to commit. Rows sharing a
        // product name become one product with N variants; categories and suppliers are created
        // by name on the fly; opening stock writes 'adjustment' movements. Products that already
        // exist by name are rejected rather than merged (no silent updates to a live catalog).
        [HttpPost("ImportCsv")]
        public async Task<IActionResult> ImportCsv([FromBody] ImportShopCsvRequest req, [FromQuery] bool dryRun = true)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!_tenantContext.Tenant.BikeShopEnabled)
                return new ApiResponses().BadRequestResult("The bike shop isn't turned on for this track.");

            var (products, errors) = ParseImportCsv(req.Csv);

            // Validate against the live catalog: duplicate names, SKUs, and barcodes.
            var existing = await _shop.ListProducts(TenantId, activeOnly: false);
            var existingNames = existing.Select(p => p.Name.Trim().ToLowerInvariant()).ToHashSet();
            var existingSkus = existing.SelectMany(p => p.Variants)
                .Where(v => !string.IsNullOrWhiteSpace(v.Sku)).Select(v => v.Sku!.Trim().ToLowerInvariant()).ToHashSet();
            var existingBarcodes = existing.SelectMany(p => p.Variants)
                .Where(v => !string.IsNullOrWhiteSpace(v.Barcode)).Select(v => v.Barcode!.Trim()).ToHashSet();
            foreach (var p in products)
            {
                if (existingNames.Contains(p.Name.Trim().ToLowerInvariant()))
                    errors.Add($"\"{p.Name}\": a product with this name already exists. Rename it in the file or delete the existing one.");
                foreach (var v in p.Variants)
                {
                    if (v.Sku is not null && existingSkus.Contains(v.Sku.Trim().ToLowerInvariant()))
                        errors.Add($"\"{p.Name}\": SKU {v.Sku} is already used by an existing product.");
                    if (v.Barcode is not null && existingBarcodes.Contains(v.Barcode.Trim()))
                        errors.Add($"\"{p.Name}\": barcode {v.Barcode} is already used by an existing product.");
                }
            }

            if (dryRun || errors.Count > 0)
            {
                return errors.Count > 0 && !dryRun
                    ? new ApiResponses().BadRequestResult("The file has errors. Fix them and try again.")
                    : new ApiResponses().OkResult(new
                    {
                        dryRun = true,
                        products = products.Count,
                        variants = products.Sum(p => p.Variants.Count),
                        newCategories = products.Select(p => p.CategoryName).Where(c => c != null).Distinct().Count(),
                        newSuppliers = products.Select(p => p.SupplierName).Where(s => s != null).Distinct().Count(),
                        errors,
                    });
            }
            if (products.Count == 0)
                return new ApiResponses().BadRequestResult("The file has no product rows.");

            var result = await _shop.ImportCatalog(TenantId, products, UserId);
            return new ApiResponses().OkResult(new
            {
                dryRun = false,
                products = result.Products,
                variants = result.Variants,
                newCategories = result.NewCategories,
                newSuppliers = result.NewSuppliers,
                errors = new List<string>(),
            });
        }

        [HttpPost("Products/{id:guid}/GenerateVariants")]
        public async Task<IActionResult> GenerateVariants(Guid id, [FromBody] GenerateShopVariantsRequest req)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var product = await _shop.GetProduct(id, TenantId);
            if (product is null) return new ApiResponses().NotFoundResult("Product not found.");

            var sizes = req.Sizes.Select(s => s.Trim()).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var colors = req.Colors.Select(c => c.Trim()).Where(c => c.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (sizes.Count == 0 && colors.Count == 0)
                return new ApiResponses().BadRequestResult("Enter at least one size or color.");
            if (sizes.Count * Math.Max(1, colors.Count) > 200 || colors.Count * Math.Max(1, sizes.Count) > 200)
                return new ApiResponses().BadRequestResult("That matrix is over 200 variants. Split it into smaller batches.");

            var combos = new List<(string? Size, string? Color)>();
            foreach (var size in sizes.Count > 0 ? sizes : new List<string> { null! })
                foreach (var color in colors.Count > 0 ? colors : new List<string> { null! })
                    combos.Add((size, color));

            var (created, skipped) = await _shop.GenerateVariants(TenantId, id, combos,
                Blank(req.SkuPrefix), req.SalePriceCents, req.CostCents, req.DepositCents, req.LowStockThreshold);
            return new ApiResponses().OkResult(new { created, skipped });
        }

        // Header-driven CSV parse into grouped products. Returns row-level errors with 1-based
        // line numbers (header = line 1) so the fix-up loop in a spreadsheet is painless.
        private static (List<ShopImportProduct> Products, List<string> Errors) ParseImportCsv(string csv)
        {
            var errors = new List<string>();
            var rows = SplitCsv(csv);
            if (rows.Count < 2)
            {
                errors.Add("The file needs a header row plus at least one product row.");
                return (new List<ShopImportProduct>(), errors);
            }

            // Header mapping: case/space/underscore-insensitive.
            static string Norm(string h) => h.Trim().ToLowerInvariant().Replace(" ", "").Replace("_", "");
            var header = rows[0].Select(Norm).ToList();
            int Col(params string[] names) => header.FindIndex(h => names.Contains(h));
            var cProduct = Col("product", "productname", "name");
            if (cProduct < 0)
            {
                errors.Add("The header row needs a \"Product\" column.");
                return (new List<ShopImportProduct>(), errors);
            }
            var cDesc = Col("description");
            var cBrand = Col("brand");
            var cCategory = Col("category");
            var cSupplier = Col("supplier", "vendor");
            var cSku = Col("sku");
            var cBarcode = Col("barcode", "upc");
            var cSize = Col("size");
            var cColor = Col("color", "colour");
            var cGender = Col("gender");
            var cPrice = Col("price", "saleprice");
            var cCost = Col("cost", "unitcost");
            var cDailyRate = Col("dailyrate", "rentalrate");
            var cDeposit = Col("deposit");
            var cTracking = Col("tracking", "trackingkind");
            var cStock = Col("stock", "quantity", "qty", "onhand");
            var cLow = Col("lowstockat", "lowstock", "reorderpoint");

            string? Cell(List<string> row, int idx) =>
                idx >= 0 && idx < row.Count && !string.IsNullOrWhiteSpace(row[idx]) ? row[idx].Trim() : null;

            var products = new List<ShopImportProduct>();
            var byName = new Dictionary<string, ShopImportProduct>();
            var seenSkus = new HashSet<string>();
            var seenBarcodes = new HashSet<string>();

            for (var i = 1; i < rows.Count; i++)
            {
                var row = rows[i];
                var line = i + 1;
                if (row.All(string.IsNullOrWhiteSpace)) continue;

                var name = Cell(row, cProduct);
                if (name is null) { errors.Add($"Line {line}: no product name."); continue; }

                int? Money(int idx, string label)
                {
                    var raw = Cell(row, idx);
                    if (raw is null) return null;
                    if (decimal.TryParse(raw.TrimStart('$'), out var dollars) && dollars >= 0 && dollars < 1_000_000)
                        return (int)Math.Round(dollars * 100, MidpointRounding.AwayFromZero);
                    errors.Add($"Line {line}: {label} \"{raw}\" isn't a dollar amount.");
                    return null;
                }
                int? Whole(int idx, string label)
                {
                    var raw = Cell(row, idx);
                    if (raw is null) return null;
                    if (int.TryParse(raw, out var n) && n >= 0 && n <= 1_000_000) return n;
                    errors.Add($"Line {line}: {label} \"{raw}\" isn't a whole number.");
                    return null;
                }

                var tracking = (Cell(row, cTracking) ?? "pool").ToLowerInvariant();
                if (tracking is not ("pool" or "serialized"))
                {
                    errors.Add($"Line {line}: tracking must be \"pool\" or \"serialized\".");
                    continue;
                }
                var stock = Whole(cStock, "stock") ?? 0;
                if (tracking == "serialized" && stock > 0)
                {
                    errors.Add($"Line {line}: serialized products import with 0 stock; add each unit (with its serial) after import.");
                    continue;
                }

                var sku = Cell(row, cSku);
                if (sku is not null && !seenSkus.Add(sku.ToLowerInvariant()))
                {
                    errors.Add($"Line {line}: SKU {sku} appears more than once in the file.");
                    continue;
                }
                var barcode = Cell(row, cBarcode);
                if (barcode is not null && !seenBarcodes.Add(barcode))
                {
                    errors.Add($"Line {line}: barcode {barcode} appears more than once in the file.");
                    continue;
                }

                var variant = new ShopImportVariant
                {
                    Sku = sku,
                    Barcode = barcode,
                    Size = Cell(row, cSize),
                    Color = Cell(row, cColor),
                    Gender = Cell(row, cGender),
                    SalePriceCents = Money(cPrice, "price"),
                    CostCents = Money(cCost, "cost"),
                    DailyRateCents = Money(cDailyRate, "daily rate"),
                    DepositCents = Money(cDeposit, "deposit") ?? 0,
                    TrackingKind = tracking,
                    Stock = stock,
                    LowStockThreshold = Whole(cLow, "low stock at"),
                };

                var key = name.ToLowerInvariant();
                if (!byName.TryGetValue(key, out var product))
                {
                    product = new ShopImportProduct
                    {
                        Name = name,
                        Description = Cell(row, cDesc),
                        Brand = Cell(row, cBrand),
                        CategoryName = Cell(row, cCategory),
                        SupplierName = Cell(row, cSupplier),
                    };
                    byName[key] = product;
                    products.Add(product);
                }
                if (product.Variants.Any(v =>
                        string.Equals(v.Size, variant.Size, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(v.Color, variant.Color, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(v.Gender, variant.Gender, StringComparison.OrdinalIgnoreCase)))
                {
                    errors.Add($"Line {line}: \"{name}\" already has a row with this size/color/gender.");
                    continue;
                }
                product.Variants.Add(variant);
            }
            return (products, errors);
        }

        // Minimal RFC-4180-ish CSV split: quoted fields, doubled quotes, CR/LF line ends.
        private static List<List<string>> SplitCsv(string text)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var field = new System.Text.StringBuilder();
            var inQuotes = false;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (inQuotes)
                {
                    if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else if (c == '"') inQuotes = false;
                    else field.Append(c);
                }
                else if (c == '"') inQuotes = true;
                else if (c == ',') { row.Add(field.ToString()); field.Clear(); }
                else if (c is '\n' or '\r')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    row.Add(field.ToString()); field.Clear();
                    rows.Add(row); row = new List<string>();
                }
                else field.Append(c);
            }
            if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row); }
            return rows;
        }

        [HttpPost("PurchaseOrderLines/{lineId:guid}/Receive")]
        public async Task<IActionResult> ReceiveLine(Guid lineId, [FromBody] ReceivePurchaseOrderLineRequest req)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var line = await _shop.GetPurchaseOrderLine(lineId, TenantId);
            if (line is null) return new ApiResponses().NotFoundResult("Purchase order line not found.");
            if (req.Quantity < 1) return new ApiResponses().BadRequestResult("Receive at least one unit.");
            if (line.QuantityReceived + req.Quantity > line.QuantityOrdered)
                return new ApiResponses().BadRequestResult(
                    $"That's more than remains on this line ({line.QuantityOrdered - line.QuantityReceived} left).");

            var variant = await _shop.GetVariant(line.VariantId, TenantId);
            List<(string, string?)>? units = null;
            if (variant?.TrackingKind == "serialized")
            {
                if (req.SerialUnits is null || req.SerialUnits.Count != req.Quantity)
                    return new ApiResponses().BadRequestResult(
                        "This is a serialized product — provide the label (and optional serial) for each unit received.");
                units = req.SerialUnits.Select(u => (u.Label.Trim(), Blank(u.Serial))).ToList();
            }

            var ok = await _shop.ReceivePurchaseOrderLine(lineId, TenantId, req.Quantity, units, UserId);
            if (!ok)
                return new ApiResponses().BadRequestResult("Could not receive this line. It may have changed — reload the purchase order.");

            // Special orders riding this PO line: stamp arrivals, consume parts for committed
            // jobs, advance awaiting_parts orders, and tell each customer their parts are in.
            var arrivals = await _shop.ProcessArrivalsForPoLine(lineId, TenantId, UserId);
            foreach (var a in arrivals)
            {
                if (string.IsNullOrWhiteSpace(a.CustomerEmail) || !_emailer.IsConfigured) continue;
                var tenant = _tenantContext.Tenant;
                static string Enc(string s) => System.Net.WebUtility.HtmlEncode(s);
                var bike = a.CustomerBikeDesc ?? "your service order";
                var body = a.NewStatus == "ready"
                    ? $"<p>Good news: the parts for {Enc(bike)} have arrived and your order is ready for pickup.</p>"
                    : $"<p>Good news: the parts for {Enc(bike)} have arrived. We'll get to work and let you know when it's ready.</p>";
                var html =
                    $"<div style=\"font-family:Arial,Helvetica,sans-serif;max-width:480px\">" +
                    $"<h2 style=\"margin:0 0 8px\">{Enc(tenant.DisplayName)}</h2>" +
                    $"<p>Hi {Enc(a.CustomerName)},</p>{body}</div>";
                try
                {
                    await _emailer.Send(a.CustomerEmail!, $"{tenant.DisplayName}: your parts are in",
                        html, null, Services.Email.TenantEmailIdentity.For(tenant));
                }
                catch { /* notification is best-effort; the receipt itself already landed */ }
            }
            return new ApiResponses().OkResult(new { workOrdersUpdated = arrivals.Count });
        }

        // ── Tax categories ────────────────────────────────────────────────────────
        [HttpGet("TaxCategories")]
        public async Task<IActionResult> ListTaxCategories([FromQuery] bool activeOnly = false)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            return new ApiResponses().OkResult(await _shop.ListTaxCategories(TenantId, activeOnly));
        }

        [HttpPost("TaxCategories")]
        public async Task<IActionResult> CreateTaxCategory([FromBody] UpsertShopTaxCategoryRequest req)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var id = await _shop.CreateTaxCategory(new ShopTaxCategory
            {
                TenantId = TenantId, Name = req.Name.Trim(), RateBps = req.RateBps,
                IsDefault = req.IsDefault, SortOrder = req.SortOrder, IsActive = req.IsActive,
            });
            return new ApiResponses().OkResult(new { id });
        }

        [HttpPut("TaxCategories/{id:guid}")]
        public async Task<IActionResult> UpdateTaxCategory(Guid id, [FromBody] UpsertShopTaxCategoryRequest req)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var n = await _shop.UpdateTaxCategory(new ShopTaxCategory
            {
                Id = id, TenantId = TenantId, Name = req.Name.Trim(), RateBps = req.RateBps,
                IsDefault = req.IsDefault, SortOrder = req.SortOrder, IsActive = req.IsActive,
            });
            return n == 0 ? new ApiResponses().NotFoundResult("Tax category not found.") : new ApiResponses().OkResult();
        }

        // ── Stock takes ───────────────────────────────────────────────────────────
        [HttpGet("StockCounts")]
        public async Task<IActionResult> ListStockCounts([FromQuery] int limit = 50)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            return new ApiResponses().OkResult(await _shop.ListStockCounts(TenantId, Math.Clamp(limit, 1, 200)));
        }

        [HttpGet("StockCounts/{id:guid}")]
        public async Task<IActionResult> GetStockCount(Guid id)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var count = await _shop.GetStockCount(id, TenantId);
            return count is null ? new ApiResponses().NotFoundResult("Stock take not found.") : new ApiResponses().OkResult(count);
        }

        [HttpPost("StockCounts")]
        public async Task<IActionResult> CreateStockCount([FromBody] CreateStockCountRequest req)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var id = await _shop.CreateStockCount(TenantId, UserId, Blank(req.Notes));
            return new ApiResponses().OkResult(new { id });
        }

        [HttpPut("StockCountLines/{lineId:guid}")]
        public async Task<IActionResult> SetStockCountLine(Guid lineId, [FromBody] SetStockCountLineRequest req)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var n = await _shop.SetStockCountLine(lineId, TenantId, req.CountedQty);
            return n == 0
                ? new ApiResponses().BadRequestResult("Could not save — the count may already be completed.")
                : new ApiResponses().OkResult();
        }

        [HttpPost("StockCounts/{id:guid}/Complete")]
        public async Task<IActionResult> CompleteStockCount(Guid id)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var ok = await _shop.CompleteStockCount(id, TenantId, UserId);
            return ok
                ? new ApiResponses().OkResult()
                : new ApiResponses().BadRequestResult("Only an open stock take can be completed.");
        }

        [HttpPost("StockCounts/{id:guid}/Cancel")]
        public async Task<IActionResult> CancelStockCount(Guid id)
        {
            if (NoTenant) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var n = await _shop.CancelStockCount(id, TenantId);
            return n == 0
                ? new ApiResponses().BadRequestResult("Only an open stock take can be cancelled.")
                : new ApiResponses().OkResult();
        }

        // ── Helpers ───────────────────────────────────────────────────────────────
        private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        // Turns a unique-index violation (duplicate SKU / barcode / serial) into a clear 400 instead
        // of a 500 from the raw Postgres error.
        private static async Task<IActionResult> GuardUnique(Func<Task<IActionResult>> action)
        {
            try { return await action(); }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                var what = ex.ConstraintName switch
                {
                    "uk_shop_variant_sku" => "That SKU is already used by another variant.",
                    "uk_shop_variant_barcode" => "That barcode is already used by another variant.",
                    "uk_shop_variant_attrs" => "A variant with those exact options already exists on this product.",
                    "uk_shop_item_serial" => "That serial number is already on another unit.",
                    _ => "That value must be unique and is already in use.",
                };
                return new ApiResponses().BadRequestResult(what);
            }
        }
    }
}
