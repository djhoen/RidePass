using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Services.Concessions;
using Services.Helpers;
using Services.Helpers.Interfaces;
using Services.LoamPassMx;
using Services.Notifications;
using Services.Payments;
using Services.Repositories.Data.ConcessionData;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Data.UserData;
using Services.Repositories.Interfaces;
using Services.Storage;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Concession;
using webapi.Payments;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    /// <summary>
    /// Concessions / store: a standalone in-person storefront (food, drink, swag) the
    /// cashier rings up via the mobile tap-to-pay app. Admin endpoints (CatalogManage)
    /// manage the catalog; the ConcessionsCounter endpoints back the cashier app: list items and
    /// take a card-present sale to an anonymous buyer. Card-present payment reuses the same
    /// Stripe Terminal flow as the counter, and the existing payment webhook finalizes the sale.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class ConcessionController : ControllerBase
    {
        private readonly IConcessionRepository _concessions;
        private readonly IPaymentProvider _payments;
        private readonly IImageStorage _imageStorage;
        private readonly ITenantRepository _tenants;
        private readonly IFeeCalculator _feeCalculator;
        private readonly ITenantLedgerRepository _ledger;
        private readonly IUserRepository _users;
        private readonly IPasswordHasher<User> _passwordHasher;
        private readonly ISeasonPassRepository _seasonPasses;
        private readonly IRiderLoampassLinkRepository _loampassLinks;
        private readonly ILoamPassMxService _loampass;
        private readonly ISmsSender _sms;
        private readonly ISmtpEmailer _emailer;
        private readonly IStripePurchaseFinalizer _finalizer;
        private readonly INotificationService _notifications;
        private readonly IEventRepository _events;
        private readonly webapi.Security.IManagerPinService _managerPin;
        private readonly ITenantCreditRepository _credit;
        private readonly ILogger<ConcessionController> _logger;
        private readonly Services.Audit.IAuditLogger _audit;
        private readonly IDiscountPresetRepository _discounts;
        private readonly Services.Pricing.ISeasonPassPerkResolver _perks;
        private readonly ITenantContext _tenantContext;
        // Per-tab advisory locks (see the open-tabs section): every mutation of one tab serializes.
        private readonly IDbHelper _db;

        public ConcessionController(
            IConcessionRepository concessions,
            IPaymentProvider payments,
            IImageStorage imageStorage,
            ITenantRepository tenants,
            IFeeCalculator feeCalculator,
            ITenantLedgerRepository ledger,
            IUserRepository users,
            IPasswordHasher<User> passwordHasher,
            ISeasonPassRepository seasonPasses,
            IRiderLoampassLinkRepository loampassLinks,
            ILoamPassMxService loampass,
            ISmsSender sms,
            ISmtpEmailer emailer,
            IStripePurchaseFinalizer finalizer,
            INotificationService notifications,
            IEventRepository events,
            webapi.Security.IManagerPinService managerPin,
            ITenantCreditRepository credit,
            ILogger<ConcessionController> logger,
            ITenantContext tenantContext,
            Services.Audit.IAuditLogger audit,
            IDiscountPresetRepository discounts,
            Services.Pricing.ISeasonPassPerkResolver perks,
            IDbHelper db)
        {
            _db = db;
            _discounts = discounts;
            _perks = perks;
            _audit = audit;
            _credit = credit;
            _logger = logger;
            _concessions = concessions;
            _payments = payments;
            _imageStorage = imageStorage;
            _tenants = tenants;
            _feeCalculator = feeCalculator;
            _ledger = ledger;
            _users = users;
            _passwordHasher = passwordHasher;
            _seasonPasses = seasonPasses;
            _loampassLinks = loampassLinks;
            _loampass = loampass;
            _sms = sms;
            _emailer = emailer;
            _finalizer = finalizer;
            _notifications = notifications;
            _events = events;
            _managerPin = managerPin;
            _tenantContext = tenantContext;
        }

        // After a sale depletes stock, alert F&B managers + admins about any item that just went low.
        // Best-effort + de-duped (each low episode notifies once) so it never blocks the sale.
        private async Task NotifyLowStock(Guid tenantId)
        {
            try
            {
                var low = await _concessions.MarkAndGetNewlyLowStock(tenantId);
                if (low.Count == 0) return;
                var names = string.Join(", ", low.Select(i => i.Name));
                var title = low.Count == 1 ? "1 item low on stock" : $"{low.Count} items low on stock";
                await _notifications.EmitToTenantRoles(tenantId, new[] { "tenant_manager", "tenant_admin" },
                    NotificationKinds.LowStock, title, $"Running low: {names}.", "/Admin/Concessions");
            }
            catch { /* alerting is best-effort */ }
        }

        // ── Admin: products ─────────────────────────────────────────────────────
        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpGet("Products/Admin")]
        public async Task<IActionResult> ListForAdmin()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var products = await _concessions.ListProducts(_tenantContext.TenantId, activeOnly: false);
            return new ApiResponses().OkResult(await Hydrate(products, activeOnly: false));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost("Products")]
        public async Task<IActionResult> Create([FromBody] UpsertConcessionProductRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var (stationId, groupIds) = await ValidateStationAndGroups(_tenantContext.TenantId, req.StationId, req.ModifierGroupIds);
            var categoryId = await ValidateCategory(_tenantContext.TenantId, req.CategoryId);
            var p = new ConcessionProduct
            {
                TenantId = _tenantContext.TenantId,
                Name = req.Name.Trim(),
                Description = Blank(req.Description),
                CategoryId = categoryId,
                PriceCents = req.PriceCents,
                ImageUrl = Blank(req.ImageUrl),
                ShowInCarousel = req.ShowInCarousel,
                IsActive = req.IsActive,
                SortOrder = req.SortOrder,
                StationId = stationId,
                RequiresPrep = req.RequiresPrep,
                ComboAvailable = req.ComboAvailable,
                Inventory = req.Inventory,
                TaxCategoryId = await ValidateTaxCategory(_tenantContext.TenantId, req.TaxCategoryId),
            };
            p.Id = await _concessions.CreateProduct(p);
            await _concessions.SetProductGroups(p.Id, groupIds);
            await _concessions.SetProductDefaultOptions(p.Id, await ValidDefaultOptions(groupIds, req.DefaultModifierOptionIds));
            return new ApiResponses().OkResult(ToProductResponse(p, new()));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPut("Products/{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpsertConcessionProductRequest req)
        {
            var existing = await _concessions.GetProduct(id, _tenantContext.TenantId);
            if (existing is null) return new ApiResponses().NotFoundResult("Item not found.");
            var (stationId, groupIds) = await ValidateStationAndGroups(_tenantContext.TenantId, req.StationId, req.ModifierGroupIds);
            existing.Name = req.Name.Trim();
            existing.Description = Blank(req.Description);
            existing.CategoryId = await ValidateCategory(_tenantContext.TenantId, req.CategoryId);
            existing.PriceCents = req.PriceCents;
            existing.ImageUrl = Blank(req.ImageUrl);
            existing.ShowInCarousel = req.ShowInCarousel;
            existing.IsActive = req.IsActive;
            existing.SortOrder = req.SortOrder;
            existing.StationId = stationId;
            existing.RequiresPrep = req.RequiresPrep;
            existing.ComboAvailable = req.ComboAvailable;
            existing.Inventory = req.Inventory;
            existing.TaxCategoryId = await ValidateTaxCategory(_tenantContext.TenantId, req.TaxCategoryId);
            await _concessions.UpdateProduct(existing);
            await _concessions.SetProductGroups(existing.Id, groupIds);
            await _concessions.SetProductDefaultOptions(existing.Id, await ValidDefaultOptions(groupIds, req.DefaultModifierOptionIds));
            var variants = await _concessions.ListVariants(existing.Id);
            var sold = await _concessions.SumSoldVariants(variants.Select(v => v.Id));
            var groups = await BuildGroupResponses(_tenantContext.TenantId, new[] { existing.Id });
            return new ApiResponses().OkResult(ToProductResponse(existing, variants, sold, groups.GetValueOrDefault(existing.Id, new())));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpDelete("Products/{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            try { await _concessions.DeleteProduct(id, _tenantContext.TenantId); }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23503")
            {
                return new ApiResponses().BadRequestResult(
                    "This item has sales on file and can't be deleted. Set it inactive instead.");
            }
            return new ApiResponses().OkResult();
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost("Products/Reorder")]
        public async Task<IActionResult> Reorder([FromBody] ReorderConcessionProductsRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (req.Items.Count == 0) return new ApiResponses().OkResult();
            await _concessions.UpdateProductSortOrders(_tenantContext.TenantId,
                req.Items.Select(i => i.Id).ToList(), req.Items.Select(i => i.SortOrder).ToList());
            return new ApiResponses().OkResult();
        }

        // ── Admin: variants ─────────────────────────────────────────────────────
        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost("Products/{productId:guid}/Variants")]
        public async Task<IActionResult> CreateVariant(Guid productId, [FromBody] UpsertConcessionVariantRequest req)
        {
            var product = await _concessions.GetProduct(productId, _tenantContext.TenantId);
            if (product is null) return new ApiResponses().NotFoundResult("Item not found.");
            var v = new ConcessionVariant
            {
                ProductId = productId,
                Size = Blank(req.Size),
                Color = Blank(req.Color),
                PriceCents = req.PriceCents,
                ImageUrl = Blank(req.ImageUrl),
                Inventory = req.Inventory,
                IsActive = req.IsActive,
                SortOrder = req.SortOrder,
            };
            try { v.Id = await _concessions.CreateVariant(v); }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
            {
                return new ApiResponses().BadRequestResult("A variant with the same size / color already exists.");
            }
            return new ApiResponses().OkResult(ToVariantResponse(v, sold: 0));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPut("Products/{productId:guid}/Variants/{variantId:guid}")]
        public async Task<IActionResult> UpdateVariant(Guid productId, Guid variantId, [FromBody] UpsertConcessionVariantRequest req)
        {
            var product = await _concessions.GetProduct(productId, _tenantContext.TenantId);
            if (product is null) return new ApiResponses().NotFoundResult("Item not found.");
            var existing = await _concessions.GetVariant(variantId);
            if (existing is null || existing.ProductId != productId)
                return new ApiResponses().NotFoundResult("Variant not found.");
            existing.Size = Blank(req.Size);
            existing.Color = Blank(req.Color);
            existing.PriceCents = req.PriceCents;
            existing.ImageUrl = Blank(req.ImageUrl);
            existing.Inventory = req.Inventory;
            existing.IsActive = req.IsActive;
            existing.SortOrder = req.SortOrder;
            try { await _concessions.UpdateVariant(existing); }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
            {
                return new ApiResponses().BadRequestResult("A variant with the same size / color already exists.");
            }
            var sold = await _concessions.SumSoldVariant(existing.Id);
            return new ApiResponses().OkResult(ToVariantResponse(existing, sold));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpDelete("Products/{productId:guid}/Variants/{variantId:guid}")]
        public async Task<IActionResult> DeleteVariant(Guid productId, Guid variantId)
        {
            var product = await _concessions.GetProduct(productId, _tenantContext.TenantId);
            if (product is null) return new ApiResponses().NotFoundResult("Item not found.");
            var existing = await _concessions.GetVariant(variantId);
            if (existing is null || existing.ProductId != productId)
                return new ApiResponses().NotFoundResult("Variant not found.");
            try { await _concessions.DeleteVariant(variantId); }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23503")
            {
                return new ApiResponses().BadRequestResult(
                    "This variant has sales on file and can't be deleted. Set it inactive instead.");
            }
            return new ApiResponses().OkResult();
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost("Image")]
        [RequestSizeLimit(5 * 1024 * 1024)]
        public async Task<IActionResult> UploadImage(IFormFile file, CancellationToken ct)
        {
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
            var url = await _imageStorage.SaveAsync(stream, _tenantContext.TenantId, "concession", ext, ct);
            return new ApiResponses().OkResult(new { imageUrl = url });
        }

        // ── Admin: stations ─────────────────────────────────────────────────────
        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpGet("Stations")]
        public async Task<IActionResult> ListStations()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var stations = await _concessions.ListStations(_tenantContext.TenantId, activeOnly: false);
            return new ApiResponses().OkResult(stations.Select(ToStationResponse).ToList());
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost("Stations")]
        public async Task<IActionResult> CreateStation([FromBody] ConcessionStationRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var s = new ConcessionStation
            {
                TenantId = _tenantContext.TenantId,
                Name = req.Name.Trim(),
                SortOrder = req.SortOrder,
                IsActive = req.IsActive,
            };
            s.Id = await _concessions.CreateStation(s);
            return new ApiResponses().OkResult(ToStationResponse(s));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPut("Stations/{id:guid}")]
        public async Task<IActionResult> UpdateStation(Guid id, [FromBody] ConcessionStationRequest req)
        {
            var existing = (await _concessions.ListStations(_tenantContext.TenantId, activeOnly: false))
                .FirstOrDefault(s => s.Id == id);
            if (existing is null) return new ApiResponses().NotFoundResult("Station not found.");
            existing.Name = req.Name.Trim();
            existing.SortOrder = req.SortOrder;
            existing.IsActive = req.IsActive;
            await _concessions.UpdateStation(existing);
            return new ApiResponses().OkResult(ToStationResponse(existing));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpDelete("Stations/{id:guid}")]
        public async Task<IActionResult> DeleteStation(Guid id)
        {
            await _concessions.DeleteStation(id, _tenantContext.TenantId);
            return new ApiResponses().OkResult();
        }

        // ── Admin: kitchen ticket printers ──────────────────────────────────────
        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpGet("Printers")]
        public async Task<IActionResult> ListPrinters()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var printers = await _concessions.ListPrinters(_tenantContext.TenantId, activeOnly: false);
            return new ApiResponses().OkResult(printers.Select(ToPrinterResponse).ToList());
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost("Printers")]
        public async Task<IActionResult> CreatePrinter([FromBody] ConcessionPrinterRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var urlError = ValidatePrinterUrl(req.Url);
            if (urlError is not null) return new ApiResponses().BadRequestResult(urlError);

            var p = new ConcessionPrinter
            {
                TenantId = _tenantContext.TenantId,
                Name = req.Name.Trim(),
                Url = req.Url.Trim().TrimEnd('/'),
                SortOrder = req.SortOrder,
                IsActive = req.IsActive,
                StationIds = req.StationIds ?? new List<Guid>(),
            };
            p.Id = await _concessions.CreatePrinter(p);
            return new ApiResponses().OkResult(ToPrinterResponse(p));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPut("Printers/{id:guid}")]
        public async Task<IActionResult> UpdatePrinter(Guid id, [FromBody] ConcessionPrinterRequest req)
        {
            // Read through the tenant-scoped list so another tenant's printer id resolves to nothing.
            var existing = (await _concessions.ListPrinters(_tenantContext.TenantId, activeOnly: false))
                .FirstOrDefault(p => p.Id == id);
            if (existing is null) return new ApiResponses().NotFoundResult("Printer not found.");

            var urlError = ValidatePrinterUrl(req.Url);
            if (urlError is not null) return new ApiResponses().BadRequestResult(urlError);

            existing.Name = req.Name.Trim();
            existing.Url = req.Url.Trim().TrimEnd('/');
            existing.SortOrder = req.SortOrder;
            existing.IsActive = req.IsActive;
            existing.StationIds = req.StationIds ?? new List<Guid>();
            await _concessions.UpdatePrinter(existing);
            return new ApiResponses().OkResult(ToPrinterResponse(existing));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpDelete("Printers/{id:guid}")]
        public async Task<IActionResult> DeletePrinter(Guid id)
        {
            await _concessions.DeletePrinter(id, _tenantContext.TenantId);
            return new ApiResponses().OkResult();
        }

        // Active printers for the cashier, which is what actually sends the tickets (the API is in
        // the cloud and cannot reach a printer on the venue LAN). ConcessionsCounter, not admin.
        [Authorize(Policy = TenantPermissions.Policy.ConcessionsCounter)]
        [HttpGet("Printers/Active")]
        public async Task<IActionResult> ListActivePrinters()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var printers = await _concessions.ListPrinters(_tenantContext.TenantId, activeOnly: true);
            return new ApiResponses().OkResult(printers.Select(ToPrinterResponse).ToList());
        }

        // Rejects an http:// address at save time rather than letting the cashier discover it as a
        // silently-dead printer: the POS is served over https, so the browser blocks mixed content.
        private static string? ValidatePrinterUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "Printer address is required.";
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed))
                return "Printer address must be a full URL, e.g. https://192.168.1.50";
            if (parsed.Scheme != Uri.UriSchemeHttps)
                return "Printer address must start with https:// - the browser blocks plain http printers from the POS page.";
            return null;
        }

        private static ConcessionPrinterResponse ToPrinterResponse(ConcessionPrinter p) => new()
        {
            Id = p.Id,
            Name = p.Name,
            Url = p.Url,
            SortOrder = p.SortOrder,
            IsActive = p.IsActive,
            StationIds = p.StationIds,
        };

        // Load the editable starter catalog on demand (categories, stations, modifier groups, sample
        // products). Idempotent by name, so it fills in only what's missing and never duplicates.
        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost("SeedStarter")]
        public async Task<IActionResult> SeedStarter()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            await _concessions.SeedStarterCatalog(_tenantContext.TenantId, onlyIfEmpty: false);
            await _concessions.MarkStarterSeeded(_tenantContext.TenantId);
            return new ApiResponses().OkResult();
        }

        // ── Categories ──────────────────────────────────────────────────────────
        // Active categories, readable by any authenticated tenant user (POS tabs, menu board + rider
        // ordering sections, and the admin item-editor dropdown). Not sensitive — shown publicly on the
        // board/order page anyway.
        [Authorize]
        [HttpGet("Categories")]
        public async Task<IActionResult> ListCategories()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var cats = await _concessions.ListCategories(_tenantContext.TenantId, activeOnly: true);
            return new ApiResponses().OkResult(cats.Select(ToCategoryResponse).ToList());
        }

        // All categories (incl. inactive) for the admin manager.
        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpGet("Categories/Admin")]
        public async Task<IActionResult> ListCategoriesAdmin()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var cats = await _concessions.ListCategories(_tenantContext.TenantId, activeOnly: false);
            return new ApiResponses().OkResult(cats.Select(ToCategoryResponse).ToList());
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost("Categories")]
        public async Task<IActionResult> CreateCategory([FromBody] ConcessionCategoryRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var cat = new ConcessionCategory
            {
                TenantId = _tenantContext.TenantId,
                Name = req.Name.Trim(),
                SortOrder = req.SortOrder,
                IsActive = req.IsActive,
                MenuBoardId = await ValidateMenuBoard(_tenantContext.TenantId, req.MenuBoardId),
            };
            cat.Id = await _concessions.CreateCategory(cat);
            return new ApiResponses().OkResult(ToCategoryResponse(cat));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPut("Categories/{id:guid}")]
        public async Task<IActionResult> UpdateCategory(Guid id, [FromBody] ConcessionCategoryRequest req)
        {
            var existing = (await _concessions.ListCategories(_tenantContext.TenantId, activeOnly: false))
                .FirstOrDefault(c => c.Id == id);
            if (existing is null) return new ApiResponses().NotFoundResult("Category not found.");
            existing.Name = req.Name.Trim();
            existing.SortOrder = req.SortOrder;
            existing.IsActive = req.IsActive;
            existing.MenuBoardId = await ValidateMenuBoard(_tenantContext.TenantId, req.MenuBoardId);
            await _concessions.UpdateCategory(existing);
            return new ApiResponses().OkResult(ToCategoryResponse(existing));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpDelete("Categories/{id:guid}")]
        public async Task<IActionResult> DeleteCategory(Guid id)
        {
            await _concessions.DeleteCategory(id, _tenantContext.TenantId);
            return new ApiResponses().OkResult();
        }

        // ── Menu boards ─────────────────────────────────────────────────────────
        // One row per in-venue screen (stacked-TV style). Active boards, readable by any authenticated
        // tenant user — the board display + board chooser need them. Not sensitive (names shown on TVs).
        [Authorize]
        [HttpGet("MenuBoards")]
        public async Task<IActionResult> ListMenuBoards()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var boards = await _concessions.ListMenuBoards(_tenantContext.TenantId, activeOnly: true);
            return new ApiResponses().OkResult(boards.Select(ToMenuBoardResponse).ToList());
        }

        // All boards (incl. inactive) for the admin manager.
        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpGet("MenuBoards/Admin")]
        public async Task<IActionResult> ListMenuBoardsAdmin()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var boards = await _concessions.ListMenuBoards(_tenantContext.TenantId, activeOnly: false);
            return new ApiResponses().OkResult(boards.Select(ToMenuBoardResponse).ToList());
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost("MenuBoards")]
        public async Task<IActionResult> CreateMenuBoard([FromBody] ConcessionMenuBoardRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var board = new ConcessionMenuBoard
            {
                TenantId = _tenantContext.TenantId,
                Name = req.Name.Trim(),
                SortOrder = req.SortOrder,
                IsActive = req.IsActive,
            };
            board.Id = await _concessions.CreateMenuBoard(board);
            return new ApiResponses().OkResult(ToMenuBoardResponse(board));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPut("MenuBoards/{id:guid}")]
        public async Task<IActionResult> UpdateMenuBoard(Guid id, [FromBody] ConcessionMenuBoardRequest req)
        {
            var existing = (await _concessions.ListMenuBoards(_tenantContext.TenantId, activeOnly: false))
                .FirstOrDefault(b => b.Id == id);
            if (existing is null) return new ApiResponses().NotFoundResult("Menu board not found.");
            existing.Name = req.Name.Trim();
            existing.SortOrder = req.SortOrder;
            existing.IsActive = req.IsActive;
            await _concessions.UpdateMenuBoard(existing);
            return new ApiResponses().OkResult(ToMenuBoardResponse(existing));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpDelete("MenuBoards/{id:guid}")]
        public async Task<IActionResult> DeleteMenuBoard(Guid id)
        {
            await _concessions.DeleteMenuBoard(id, _tenantContext.TenantId);
            return new ApiResponses().OkResult();
        }

        // ── Menu board promo tiles ──────────────────────────────────────────────
        // Callout tiles rotated through the board carousel ("Make it a combo $5.99"). Active tiles,
        // readable by any authenticated tenant user (shown on the in-venue TVs anyway).
        [Authorize]
        [HttpGet("MenuPromos")]
        public async Task<IActionResult> ListMenuPromos()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var promos = await _concessions.ListMenuPromos(_tenantContext.TenantId, activeOnly: true);
            return new ApiResponses().OkResult(promos.Select(ToMenuPromoResponse).ToList());
        }

        // All promos (incl. inactive) for the admin manager.
        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpGet("MenuPromos/Admin")]
        public async Task<IActionResult> ListMenuPromosAdmin()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var promos = await _concessions.ListMenuPromos(_tenantContext.TenantId, activeOnly: false);
            return new ApiResponses().OkResult(promos.Select(ToMenuPromoResponse).ToList());
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost("MenuPromos")]
        public async Task<IActionResult> CreateMenuPromo([FromBody] ConcessionMenuPromoRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var promo = new ConcessionMenuPromo
            {
                TenantId = _tenantContext.TenantId,
                MenuBoardId = await ValidateMenuBoard(_tenantContext.TenantId, req.MenuBoardId),
                Title = req.Title.Trim(),
                Subtitle = Blank(req.Subtitle),
                ImageUrl = Blank(req.ImageUrl),
                SortOrder = req.SortOrder,
                IsActive = req.IsActive,
            };
            promo.Id = await _concessions.CreateMenuPromo(promo);
            return new ApiResponses().OkResult(ToMenuPromoResponse(promo));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPut("MenuPromos/{id:guid}")]
        public async Task<IActionResult> UpdateMenuPromo(Guid id, [FromBody] ConcessionMenuPromoRequest req)
        {
            var existing = (await _concessions.ListMenuPromos(_tenantContext.TenantId, activeOnly: false))
                .FirstOrDefault(p => p.Id == id);
            if (existing is null) return new ApiResponses().NotFoundResult("Promo not found.");
            existing.MenuBoardId = await ValidateMenuBoard(_tenantContext.TenantId, req.MenuBoardId);
            existing.Title = req.Title.Trim();
            existing.Subtitle = Blank(req.Subtitle);
            existing.ImageUrl = Blank(req.ImageUrl);
            existing.SortOrder = req.SortOrder;
            existing.IsActive = req.IsActive;
            await _concessions.UpdateMenuPromo(existing);
            return new ApiResponses().OkResult(ToMenuPromoResponse(existing));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpDelete("MenuPromos/{id:guid}")]
        public async Task<IActionResult> DeleteMenuPromo(Guid id)
        {
            await _concessions.DeleteMenuPromo(id, _tenantContext.TenantId);
            return new ApiResponses().OkResult();
        }

        // ── Customer-facing POS display ─────────────────────────────────────────
        // A paired second tablet mirrors the order being rung up (read-only) and captures the
        // customer's tip. The display tablet creates a session and shows its pair code; the cashier
        // types the code into the POS once. The POS then pushes state; the display polls and posts
        // the tip back. Both devices run staff logins, so everything sits behind ConcessionsCounter.
        [Authorize(Policy = TenantPermissions.Policy.ConcessionsCounter)]
        [HttpPost("Display")]
        public async Task<IActionResult> CreateDisplay()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var pairCode = Random.Shared.Next(100000, 1000000).ToString();
            var id = await _concessions.CreateDisplay(_tenantContext.TenantId, pairCode);
            return new ApiResponses().OkResult(new ConcessionDisplayResponse { Id = id, PairCode = pairCode });
        }

        [Authorize(Policy = TenantPermissions.Policy.ConcessionsCounter)]
        [HttpGet("Display/{id:guid}")]
        public async Task<IActionResult> GetDisplay(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var d = await _concessions.GetDisplay(id, _tenantContext.TenantId);
            if (d is null) return new ApiResponses().NotFoundResult("Display not found.");
            return new ApiResponses().OkResult(ToDisplayResponse(d));
        }

        // Pairing lookup: the cashier types the code shown on the display tablet into the POS.
        [Authorize(Policy = TenantPermissions.Policy.ConcessionsCounter)]
        [HttpGet("Display/ByCode/{code}")]
        public async Task<IActionResult> GetDisplayByCode(string code)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var d = await _concessions.GetDisplayByCode(code.Trim(), _tenantContext.TenantId);
            if (d is null) return new ApiResponses().NotFoundResult("No display is showing that code. Open the customer display screen on the tablet first.");
            return new ApiResponses().OkResult(ToDisplayResponse(d));
        }

        // POS pushes the in-progress order snapshot. Also clears any previous tip selection so a
        // prior order's tip can never bleed into the next one.
        [Authorize(Policy = TenantPermissions.Policy.ConcessionsCounter)]
        [HttpPut("Display/{id:guid}/State")]
        public async Task<IActionResult> UpdateDisplayState(Guid id, [FromBody] ConcessionDisplayStateRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var d = await _concessions.GetDisplay(id, _tenantContext.TenantId);
            if (d is null) return new ApiResponses().NotFoundResult("Display not found.");
            await _concessions.UpdateDisplayState(id, _tenantContext.TenantId, req.StateJson);
            return new ApiResponses().OkResult();
        }

        // The customer's tip selection, posted by the display tablet. The POS polls it and applies it
        // to the sale (where the server re-validates against the tenant's tip settings).
        [Authorize(Policy = TenantPermissions.Policy.ConcessionsCounter)]
        [HttpPost("Display/{id:guid}/Tip")]
        public async Task<IActionResult> SetDisplayTip(Guid id, [FromBody] ConcessionDisplayTipRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var d = await _concessions.GetDisplay(id, _tenantContext.TenantId);
            if (d is null) return new ApiResponses().NotFoundResult("Display not found.");
            var settings = await _concessions.GetMenuSettings(_tenantContext.TenantId);
            if (!(settings?.TipsEnabled ?? false)) return new ApiResponses().BadRequestResult("Tips aren't enabled for this venue.");
            await _concessions.SetDisplayTip(id, _tenantContext.TenantId, req.TipCents);
            return new ApiResponses().OkResult();
        }

        // ── Menu board settings ─────────────────────────────────────────────────
        // Readable by any authenticated tenant user (board display + rider page); returns brand-fallback
        // defaults when the tenant hasn't customized it yet.
        [Authorize]
        [HttpGet("MenuSettings")]
        public async Task<IActionResult> GetMenuSettings()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var s = await _concessions.GetMenuSettings(_tenantContext.TenantId);
            return new ApiResponses().OkResult(new ConcessionMenuSettingsResponse
            {
                LogoUrl = s?.LogoUrl,
                BackgroundColor = s?.BackgroundColor,
                TextColor = s?.TextColor,
                AccentColor = s?.AccentColor,
                ShowCarousel = s?.ShowCarousel ?? true,
                CarouselSeconds = s?.CarouselSeconds ?? 5,
                TipsEnabled = s?.TipsEnabled ?? false,
                CustomerDisplayEnabled = s?.CustomerDisplayEnabled ?? false,
                PrepWarnMinutes = s?.PrepWarnMinutes ?? 5,
                PrepLateMinutes = s?.PrepLateMinutes ?? 10,
                OrderingHours = ParseOrderingHours(s?.OrderingHoursJson),
                OrderingSeasons = ParseOrderingSeasons(s?.OrderingSeasonsJson),
                RequireEventDay = s?.RequireEventDay ?? true,
                PricesIncludeTax = s?.PricesIncludeTax ?? false,
                SeasonPassDiscountEnabled = s?.SeasonPassDiscountEnabled ?? false,
                SeasonPassDiscountKind = s?.SeasonPassDiscountKind ?? "percent",
                SeasonPassDiscountValue = s?.SeasonPassDiscountValue ?? 0,
                LoampassDiscountEnabled = s?.LoampassDiscountEnabled ?? false,
                LoampassDiscountKind = s?.LoampassDiscountKind ?? "percent",
                LoampassDiscountValue = s?.LoampassDiscountValue ?? 0,
                RequireManagerForManualDiscount = s?.RequireManagerForManualDiscount ?? true,
                TabsEnabled = s?.TabsEnabled ?? true,
                TabHoldCents = s?.TabHoldCents ?? 5000,
                StarterSeeded = s?.SeededAt != null,
                OrderingOpenNow = (await EvaluateOrderingStatus(s)).OpenNow,
            });
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPut("MenuSettings")]
        public async Task<IActionResult> UpdateMenuSettings([FromBody] ConcessionMenuSettingsRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            await _concessions.UpsertMenuSettings(new ConcessionMenuSettings
            {
                TenantId = _tenantContext.TenantId,
                LogoUrl = Blank(req.LogoUrl),
                BackgroundColor = Blank(req.BackgroundColor),
                TextColor = Blank(req.TextColor),
                AccentColor = Blank(req.AccentColor),
                ShowCarousel = req.ShowCarousel,
                CarouselSeconds = req.CarouselSeconds,
                TipsEnabled = req.TipsEnabled,
                CustomerDisplayEnabled = req.CustomerDisplayEnabled,
                PrepWarnMinutes = Math.Clamp(req.PrepWarnMinutes, 1, 240),
                PrepLateMinutes = Math.Clamp(req.PrepLateMinutes, 1, 240),
                OrderingHoursJson = SerializeOrderingHours(req.OrderingHours),
                OrderingSeasonsJson = SerializeOrderingSeasons(req.OrderingSeasons),
                RequireEventDay = req.RequireEventDay,
                PricesIncludeTax = req.PricesIncludeTax,
                SeasonPassDiscountEnabled = req.SeasonPassDiscountEnabled,
                SeasonPassDiscountKind = NormalizeDiscountKind(req.SeasonPassDiscountKind),
                SeasonPassDiscountValue = Math.Max(0, req.SeasonPassDiscountValue),
                LoampassDiscountEnabled = req.LoampassDiscountEnabled,
                LoampassDiscountKind = NormalizeDiscountKind(req.LoampassDiscountKind),
                LoampassDiscountValue = Math.Max(0, req.LoampassDiscountValue),
                RequireManagerForManualDiscount = req.RequireManagerForManualDiscount,
                TabsEnabled = req.TabsEnabled,
                // Stripe's card minimum at the bottom; nobody needs a five-figure bar hold.
                TabHoldCents = Math.Clamp(req.TabHoldCents, 50, 1_000_000),
            });
            return new ApiResponses().OkResult();
        }

        // ── Tax categories ──────────────────────────────────────────────────────────
        // Readable by any authenticated tenant user (the POS/menu need rates to show tax). Ensures a
        // default category exists so there's always at least one editable row.
        [Authorize]
        [HttpGet("TaxCategories")]
        public async Task<IActionResult> ListTaxCategories()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var cats = await _concessions.ListTaxCategories(_tenantContext.TenantId);
            return new ApiResponses().OkResult(cats.Select(ToTaxCategoryResponse).ToList());
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost("TaxCategories")]
        public async Task<IActionResult> CreateTaxCategory([FromBody] ConcessionTaxCategoryRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var c = new ConcessionTaxCategory
            {
                TenantId = _tenantContext.TenantId,
                Name = req.Name.Trim(),
                RateBps = Math.Clamp(req.RateBps, 0, 10000),
                IsDefault = req.IsDefault,
                SortOrder = req.SortOrder,
                IsActive = req.IsActive,
            };
            c.Id = await _concessions.CreateTaxCategory(c);
            return new ApiResponses().OkResult(ToTaxCategoryResponse(c));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPut("TaxCategories/{id:guid}")]
        public async Task<IActionResult> UpdateTaxCategory(Guid id, [FromBody] ConcessionTaxCategoryRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var existing = (await _concessions.ListTaxCategories(_tenantContext.TenantId)).FirstOrDefault(c => c.Id == id);
            if (existing is null) return new ApiResponses().NotFoundResult("Tax category not found.");
            // Can't un-default the default directly (would leave the tenant with none); editing another
            // category to default demotes this one via the repo.
            var c = new ConcessionTaxCategory
            {
                Id = id,
                TenantId = _tenantContext.TenantId,
                Name = req.Name.Trim(),
                RateBps = Math.Clamp(req.RateBps, 0, 10000),
                IsDefault = existing.IsDefault || req.IsDefault,
                SortOrder = req.SortOrder,
                IsActive = req.IsActive,
            };
            await _concessions.UpdateTaxCategory(c);
            return new ApiResponses().OkResult(ToTaxCategoryResponse(c));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpDelete("TaxCategories/{id:guid}")]
        public async Task<IActionResult> DeleteTaxCategory(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var existing = (await _concessions.ListTaxCategories(_tenantContext.TenantId)).FirstOrDefault(c => c.Id == id);
            if (existing is null) return new ApiResponses().NotFoundResult("Tax category not found.");
            if (existing.IsDefault) return new ApiResponses().BadRequestResult("The default tax category can't be deleted.");
            await _concessions.DeleteTaxCategory(id, _tenantContext.TenantId);
            return new ApiResponses().OkResult();
        }

        // ── Discount presets ──────────────────────────────────────────────────────────
        // The discount buttons the F&B POS offers. Reads the tenant-wide discount list (Script0251)
        // filtered to this surface, so a discount defined once for "food and drink and the bike
        // shop" shows up in both places without being entered twice. Managing them moved to
        // Settings -> Discounts; this endpoint is read-only.
        [Authorize]
        [HttpGet("DiscountPresets")]
        public async Task<IActionResult> ListDiscountPresets()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var presets = await _discounts.ListForSurface(
                _tenantContext.TenantId, Services.Repositories.Data.DiscountData.DiscountSurfaces.Concession);
            return new ApiResponses().OkResult(presets.Select(p => new ConcessionDiscountPresetResponse
            {
                Id = p.Id, Name = p.Name, Kind = p.Kind, Value = p.Value,
                IsActive = p.IsActive, SortOrder = p.SortOrder, RequiresManager = p.RequiresManager,
            }).ToList());
        }

        // Creating and editing discounts moved to Settings -> Discounts (DiscountController).
        // The write endpoints that lived here are gone rather than left pointing at
        // concession_discount_preset: the POS reads the tenant-wide list now, so editing the old
        // table would have looked like it worked and changed nothing a cashier ever sees.

        // ── Comp reasons ──────────────────────────────────────────────────────────────
        [Authorize]
        [HttpGet("CompReasons")]
        public async Task<IActionResult> ListCompReasons()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var reasons = await _concessions.ListCompReasons(_tenantContext.TenantId, activeOnly: false);
            return new ApiResponses().OkResult(reasons.Select(ToCompReasonResponse).ToList());
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost("CompReasons")]
        public async Task<IActionResult> CreateCompReason([FromBody] ConcessionCompReasonRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var kind = NormalizeCompKind(req.DefaultKind);
            var c = new ConcessionCompReason
            {
                TenantId = _tenantContext.TenantId,
                Name = req.Name.Trim(),
                DefaultKind = kind,
                DefaultValue = kind == "full" ? 0 : ClampDiscountValue(kind, req.DefaultValue),
                IsActive = req.IsActive,
                SortOrder = req.SortOrder,
            };
            c.Id = await _concessions.CreateCompReason(c);
            return new ApiResponses().OkResult(ToCompReasonResponse(c));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPut("CompReasons/{id:guid}")]
        public async Task<IActionResult> UpdateCompReason(Guid id, [FromBody] ConcessionCompReasonRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var existing = await _concessions.GetCompReason(id, _tenantContext.TenantId);
            if (existing is null) return new ApiResponses().NotFoundResult("Comp reason not found.");
            var kind = NormalizeCompKind(req.DefaultKind);
            existing.Name = req.Name.Trim();
            existing.DefaultKind = kind;
            existing.DefaultValue = kind == "full" ? 0 : ClampDiscountValue(kind, req.DefaultValue);
            existing.IsActive = req.IsActive;
            existing.SortOrder = req.SortOrder;
            await _concessions.UpdateCompReason(existing);
            return new ApiResponses().OkResult(ToCompReasonResponse(existing));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpDelete("CompReasons/{id:guid}")]
        public async Task<IActionResult> DeleteCompReason(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            await _concessions.DeleteCompReason(id, _tenantContext.TenantId);
            return new ApiResponses().OkResult();
        }

        // ── Manager PIN ───────────────────────────────────────────────────────────────
        // A staff member sets/clears their own POS authorization PIN. Only managers/admins may hold one;
        // a cashier-only account is rejected. Stored as a salted hash.
        // Rate-limited like VerifyManagerPin: the uniqueness check below is an oracle ("that PIN is
        // taken" confirms a hit), so without a throttle a manager could enumerate the 4-digit space to
        // learn a colleague's PIN and forge their comp/void approvals.
        [Authorize]
        [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("manager-pin")]
        [HttpPut("ManagerPin")]
        public async Task<IActionResult> SetManagerPin([FromBody] ConcessionManagerPinRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!Guid.TryParse(User.FindFirst("UserId")?.Value, out var uid))
                return new ApiResponses().BadRequestResult("Not signed in.");
            var user = await _users.GetById(uid);
            if (user is null || user.TenantId != _tenantContext.TenantId)
                return new ApiResponses().BadRequestResult("User not found.");
            if (!IsManagerOrAdmin(user))
                return new ApiResponses().BadRequestResult("Only managers and admins can set an authorization PIN.");

            var pin = req.Pin?.Trim();
            if (string.IsNullOrEmpty(pin))
            {
                await _users.SetPosPinHash(uid, null);   // clear
                return new ApiResponses().OkResult();
            }
            if (pin.Length < 4 || pin.Length > 8 || !pin.All(char.IsDigit))
                return new ApiResponses().BadRequestResult("PIN must be 4 to 8 digits.");
            // Every manager has a distinct PIN so the authorizer on a comp/override is unambiguous.
            if (!await _managerPin.IsPinAvailableAsync(_tenantContext.TenantId, uid, pin))
                return new ApiResponses().BadRequestResult("Another manager already uses that PIN. Choose a different one.");
            await _users.SetPosPinHash(uid, _passwordHasher.HashPassword(user, pin));
            return new ApiResponses().OkResult();
        }

        // Whether the signed-in user is a manager/admin and (if so) has set a PIN. Drives the forced
        // PIN-setup prompt so every manager carries one.
        [Authorize]
        [HttpGet("ManagerPin/Status")]
        public async Task<IActionResult> ManagerPinStatus()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!Guid.TryParse(User.FindFirst("UserId")?.Value, out var uid))
                return new ApiResponses().BadRequestResult("Not signed in.");
            var user = await _users.GetById(uid);
            var isManager = user is not null && user.TenantId == _tenantContext.TenantId && IsManagerOrAdmin(user);
            var hasPin = isManager && await _managerPin.HasPinAsync(uid);
            return new ApiResponses().OkResult(new { isManager, hasPin });
        }

        // The POS confirms a PIN authorizes a gated action and shows whose approval it is. A bad PIN is a
        // 400 with a generic message so the digits never reveal which (if any) manager they hit; repeated
        // wrong guesses lock the entering user out (rate-limited + DB lockout).
        [Authorize(Policy = TenantPermissions.Policy.ConcessionsCounter)]
        [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("manager-pin")]
        [HttpPost("VerifyManagerPin")]
        public async Task<IActionResult> VerifyManagerPin([FromBody] ConcessionVerifyManagerPinRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!Guid.TryParse(User.FindFirst("UserId")?.Value, out var uid))
                return new ApiResponses().BadRequestResult("Not signed in.");
            var result = await _managerPin.VerifyAsync(_tenantContext.TenantId, uid, req.Pin);
            if (!result.Authorized)
            {
                // Failures only. A successful verification is already implied by the action it
                // authorized (which logs its own entry), but repeated failures from one staff
                // account are how PIN guessing looks from the outside, and the DB lockout alone
                // leaves no reviewable trace of who was trying. The PIN is never recorded.
                await _audit.Log(
                    "concession.manager_pin_failed",
                    "Manager PIN verification failed",
                    targetKind: "user",
                    targetId: uid,
                    tenantId: _tenantContext.TenantId,
                    metadata: new { error = result.Error });
                return new ApiResponses().BadRequestResult(result.Error ?? "That manager PIN wasn't recognized.");
            }
            return new ApiResponses().OkResult(new ConcessionManagerPinResponse
            {
                ManagerUserId = result.AuthorizedUserId!.Value,
                ManagerName = result.AuthorizedName!,
            });
        }

        // ── Member discount lookup ─────────────────────────────────────────────────────
        // Cashier enters an email/phone; we report whether the customer holds an active Season Pass and/or
        // a linked LoamPass account, plus the discount each enabled perk would apply.
        [Authorize(Policy = TenantPermissions.Policy.ConcessionsCounter)]
        [HttpGet("MemberLookup")]
        public async Task<IActionResult> MemberLookup([FromQuery] string query)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (string.IsNullOrWhiteSpace(query)) return new ApiResponses().BadRequestResult("Enter an email or phone.");
            var settings = await _concessions.GetMenuSettings(_tenantContext.TenantId);
            var (user, hasPass, hasLoam, fbGrant) = await ResolveMemberAsync(query);
            if (user is null) return new ApiResponses().OkResult(new ConcessionMemberLookupResponse { Found = false });

            ConcessionMemberPerk? PerkFor(bool eligible, bool enabled, string kind, int value, string label) =>
                !enabled ? null : new ConcessionMemberPerk
                {
                    Eligible = eligible,
                    Kind = kind,
                    Value = value,
                    Label = $"{label}: {DescribeDiscount(kind, value)}",
                };

            // Same precedence the till applies, so what the cashier is shown is what gets rung.
            var seasonPassPerk = fbGrant is not null
                ? new ConcessionMemberPerk
                {
                    Eligible = true,
                    Kind = fbGrant.Benefit.DiscountKind,
                    Value = fbGrant.Benefit.DiscountValue,
                    Label = $"{fbGrant.ProductName}: " +
                            $"{DescribeDiscount(fbGrant.Benefit.DiscountKind, fbGrant.Benefit.DiscountValue)}",
                }
                : PerkFor(hasPass, _tenantContext.Tenant.SeasonPassDiscountAppliesTo("concession"),
                    _tenantContext.Tenant.SeasonPassDiscountKind, _tenantContext.Tenant.SeasonPassDiscountValue,
                    "Season Pass");

            return new ApiResponses().OkResult(new ConcessionMemberLookupResponse
            {
                Found = true,
                CustomerName = $"{user.FirstName} {user.LastName}".Trim(),
                CustomerEmail = user.Email,
                SeasonPass = seasonPassPerk,
                Loampass = PerkFor(hasLoam, settings?.LoampassDiscountEnabled ?? false,
                    settings?.LoampassDiscountKind ?? "percent", settings?.LoampassDiscountValue ?? 0, "LoamPass"),
            });
        }

        // ── Void/comp report ────────────────────────────────────────────────────────────
        // Comped F&B sales in a window: amount comped, reason, who rang it, and the authorizing manager.
        [Authorize(Policy = TenantPermissions.Policy.ReportsView)]
        [HttpGet("Reports/Comps")]
        public async Task<IActionResult> CompReport([FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var toUtc = (to ?? DateTime.UtcNow).ToUniversalTime();
            var fromUtc = (from ?? toUtc.AddDays(-30)).ToUniversalTime();
            var comps = await _concessions.SearchComps(_tenantContext.TenantId, fromUtc, toUtc);

            // Resolve cashier names for the distinct sellers (manager name is snapshotted on the sale).
            var sellerIds = comps.Where(c => c.SoldByUserId.HasValue).Select(c => c.SoldByUserId!.Value).Distinct().ToList();
            var sellerNames = new Dictionary<Guid, string>();
            foreach (var sid in sellerIds)
            {
                var u = await _users.GetById(sid);
                if (u != null) sellerNames[sid] = $"{u.FirstName} {u.LastName}".Trim();
            }

            var rows = comps.Select(c => new ConcessionCompReportRow
            {
                SaleId = c.Id,
                OrderNumber = c.OrderNumber,
                CreatedAt = c.CreatedAt,
                DiscountCents = c.DiscountCents,
                TotalCents = c.TotalCents,
                CompReasonLabel = c.CompReasonLabel,
                CashierName = c.SoldByUserId.HasValue && sellerNames.TryGetValue(c.SoldByUserId.Value, out var n) ? n : null,
                AuthorizedByName = c.AuthorizedByName,
            }).ToList();

            return new ApiResponses().OkResult(new ConcessionCompReportResponse
            {
                Rows = rows,
                TotalCompCents = rows.Sum(r => r.DiscountCents),
                Count = rows.Count,
            });
        }

        private static ConcessionTaxCategoryResponse ToTaxCategoryResponse(ConcessionTaxCategory c) => new()
        {
            Id = c.Id,
            Name = c.Name,
            RateBps = c.RateBps,
            IsDefault = c.IsDefault,
            SortOrder = c.SortOrder,
            IsActive = c.IsActive,
        };

        // ── Discounts & comps: mappers, helpers, and the server-side apply engine ────────
        private static ConcessionDiscountPresetResponse ToDiscountPresetResponse(ConcessionDiscountPreset p) => new()
        {
            Id = p.Id, Name = p.Name, Kind = p.Kind, Value = p.Value, IsActive = p.IsActive, SortOrder = p.SortOrder,
        };

        private static ConcessionCompReasonResponse ToCompReasonResponse(ConcessionCompReason c) => new()
        {
            Id = c.Id, Name = c.Name, DefaultKind = c.DefaultKind, DefaultValue = c.DefaultValue,
            IsActive = c.IsActive, SortOrder = c.SortOrder,
        };

        private static string NormalizeDiscountKind(string? kind) =>
            string.Equals(kind?.Trim(), "amount", StringComparison.OrdinalIgnoreCase) ? "amount" : "percent";

        private static string NormalizeCompKind(string? kind)
        {
            var k = kind?.Trim().ToLowerInvariant();
            return k is "amount" or "percent" ? k : "full";
        }

        // Clamp a percent (basis points, max 100%) or an amount (non-negative cents).
        private static int ClampDiscountValue(string kind, int value) =>
            kind == "percent" ? Math.Clamp(value, 0, 10000) : Math.Max(0, value);

        // Human description of a discount config: "10%" or "$2.50".
        private static string DescribeDiscount(string kind, int value) =>
            kind == "amount" ? $"${value / 100.0:0.00}" : $"{value / 100.0:0.##}%";

        // Rejects an impossible modifier group (blank name, negative min, or max below min) before it can
        // be saved and silently block every sale of its product. Returns an error string, or null if ok.
        private static string? ValidateModifierGroup(ConcessionModifierGroupRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Name)) return "Name is required.";
            if (req.MinSelect < 0) return "Minimum selections can't be negative.";
            if (req.MaxSelect.HasValue && req.MaxSelect.Value < Math.Max(1, req.MinSelect))
                return "Maximum selections must be at least the minimum (and at least 1).";
            return null;
        }

        private static bool IsManagerOrAdmin(User u) =>
            (u.Roles ?? Array.Empty<string>()).Any(r => r is "tenant_admin" or "tenant_manager")
            || u.Role is "tenant_admin" or "tenant_manager";

        // Discount cents for a kind against a base. 'percent' = basis points, 'amount' = cents (capped at
        // base), 'full' = the whole base. Never exceeds the base. Half-up rounding for percents.
        private static int ComputeDiscountCents(string kind, int value, int baseCents)
        {
            if (baseCents <= 0 || (value <= 0 && kind != "full")) return 0;
            var cents = kind switch
            {
                "percent" => (int)Math.Round(baseCents * (Math.Clamp(value, 0, 10000) / 10000.0), MidpointRounding.AwayFromZero),
                "amount"  => value,
                "full"    => baseCents,
                _ => 0,
            };
            return Math.Clamp(cents, 0, baseCents);
        }

        // Split a total of cents across lines proportionally to weights, using largest-remainder so the
        // shares sum exactly to total (no rounding drift).
        private static int[] AllocateProportional(int total, IReadOnlyList<int> weights)
        {
            var n = weights.Count;
            var result = new int[n];
            var weightSum = weights.Sum();
            if (total <= 0 || n == 0 || weightSum <= 0) return result;
            var fracs = new (int idx, double frac)[n];
            var allocated = 0;
            for (var i = 0; i < n; i++)
            {
                var exact = total * (double)weights[i] / weightSum;
                var floor = (int)Math.Floor(exact);
                result[i] = floor;
                allocated += floor;
                fracs[i] = (i, exact - floor);
            }
            var leftover = total - allocated;
            foreach (var f in fracs.OrderByDescending(x => x.frac).Take(Math.Min(leftover, n)))
                result[f.idx]++;
            return result;
        }

        /// <summary>
        /// What the counter needs to know about a customer: who they are, whether they hold an
        /// active season pass and/or a linked LoamPass, and the best PER-PASS F&amp;B perk their
        /// pass carries.
        ///
        /// The per-pass grant is the whole point of the tuple's fourth slot. The tenant-wide
        /// "Season Pass discount" setting treats every pass alike, so a track that gives staff
        /// half-price food had no way to say so: an employee pass and a customer's season pass
        /// both came out at the same percentage. <c>ListActiveBenefitGrantsForUser</c> answers per
        /// product, and it applies the same strictness the gate would (paid, registered, waiver
        /// signed, in-window, and for an employee pass, the employee still active).
        /// </summary>
        private async Task<(User? user, bool seasonPass, bool loampass, SeasonPassBenefitGrant? fbGrant)>
            ResolveMemberAsync(string? emailOrPhone)
        {
            var q = emailOrPhone?.Trim();
            if (string.IsNullOrWhiteSpace(q)) return (null, false, false, null);
            var tenantId = _tenantContext.TenantId;
            var user = q.Contains('@')
                ? await _users.GetByEmail(tenantId, q)
                : await _users.GetByPhoneE164(NormalizeToE164(q)) ?? await _users.GetByEmail(tenantId, q);

            // GetByPhoneE164 is deliberately global (the Inbox needs it that way), so a phone
            // number can resolve to a user who belongs to a DIFFERENT tenant. Without this guard a
            // cashier could type any number and read back that person's name and email. A global
            // rider account (tenant_id IS NULL) is shared by design and still allowed through;
            // everything downstream is tenant-scoped anyway, so such a rider simply has no perks
            // here unless they hold a pass at this track.
            if (user is not null && user.TenantId is Guid ownerTenantId && ownerTenantId != tenantId)
            {
                user = null;
            }
            if (user is null) return (null, false, false, null);

            // Deliberately the SAME check the till applies (HasPassValidOn), not a local
            // status-and-window scan. A looser test here would show the cashier a perk the till
            // then refuses: a spent ride pack or a deactivated employee's pass would read as
            // eligible on the lookup and fail on the sale.
            var hasPass = await _seasonPasses.HasPassValidOn(user.Id, tenantId, DateTime.UtcNow);

            // Best F&B perk across every pass they hold. Someone with an employee pass AND a
            // customer season pass gets the better of the two rather than whichever sorted first.
            //
            // Ranked against a fixed $100 basket, NOT the sale's own subtotal (which the bike shop
            // can use because it picks inside the sale). One grant is chosen here and then used by
            // both the counter's lookup and the till, so the number the cashier is shown is always
            // the number that gets rung. Picking per-basket would let those two disagree whenever a
            // percent perk and a fixed-amount perk are held together.
            var grants = await _seasonPasses.ListActiveBenefitGrantsForUser(
                user.Id, tenantId, benefitType: "concession", scopeId: null, onDateUtc: DateTime.UtcNow);
            var fbGrant = grants
                .OrderByDescending(g => g.Benefit.DiscountFor(10_000))
                .ThenByDescending(g => g.Benefit.DiscountKind == "percent")
                .FirstOrDefault();

            var hasLoam = false;
            if (!string.IsNullOrWhiteSpace(_tenantContext.Tenant.LoampassMxDestinationId))
            {
                var links = await _loampassLinks.ListByUserId(user.Id, tenantId);
                hasLoam = links.Count > 0;
            }
            return (user, hasPass, hasLoam, fbGrant);
        }

        private static string NormalizeToE164(string raw)
        {
            var digits = new string(raw.Where(char.IsDigit).ToArray());
            if (digits.Length == 10) return "+1" + digits;
            if (digits.Length == 11 && digits.StartsWith("1")) return "+" + digits;
            return raw.StartsWith("+") ? raw : "+" + digits;
        }

        // Order-level discount summary to stamp on the sale (line-level effects are applied to the lines).
        private class DiscountOutcome
        {
            public int DiscountCents;
            public string? DiscountKind;
            public string? DiscountLabel;
            public Guid? CompReasonId;
            public string? CompReasonLabel;
            public Guid? AuthorizedByUserId;
            public string? AuthorizedByName;
            public Guid? PurchaserUserId;
            public string? PurchaserEmail;
            public string? PurchaserName;
        }

        // Resolves and applies any line- and order-level discounts/comps server-side, mutating each line's
        // net total + tax snapshot, and returns the summary to stamp on the sale. Enforces the manager-PIN
        // gate (comps always; manual percent/amount when the tenant requires it) and member eligibility.
        private async Task<(bool ok, string? error, DiscountOutcome outcome)> ApplyDiscounts(
            Guid tenantId, Guid requestingUserId, List<ConcessionSaleLine> lines, List<ConcessionSaleRequest.SaleLine> requested,
            ConcessionSaleRequest req, ConcessionMenuSettings? settings, bool pricesIncludeTax)
        {
            var outcome = new DiscountOutcome();
            var requireManagerManual = settings?.RequireManagerForManualDiscount ?? true;

            // Load every preset this sale references up front. The PIN decision happens before any
            // discount is resolved, and whether a PRESET needs a manager is now per-discount
            // configuration (Script0251) rather than a fixed rule, so that decision needs the rows
            // in hand. One lookup per distinct preset, not one per line.
            var presetIds = new[] { req.Discount }
                .Concat(requested.Select(i => i.Discount))
                .Where(d => d?.Kind == "preset" && d.PresetId is not null)
                .Select(d => d!.PresetId!.Value)
                .Distinct()
                .ToList();
            var presetsById = new Dictionary<Guid, Services.Repositories.Data.DiscountData.DiscountPreset>();
            foreach (var pid in presetIds)
            {
                var loaded = await _discounts.Get(pid, tenantId);
                if (loaded is not null) presetsById[pid] = loaded;
            }

            bool NeedsPin(ConcessionDiscountInput? d) =>
                d != null && (d.Kind == "comp"
                    || ((d.Kind == "percent" || d.Kind == "amount") && requireManagerManual)
                    // A preset carries its own answer: a track can wave through "Military 10%"
                    // while still gating "Employee 50%".
                    || (d.Kind == "preset" && d.PresetId is Guid pid
                        && presetsById.TryGetValue(pid, out var pp) && pp.RequiresManager));

            // One PIN authorizes the whole sale. Verify up front if anything needs it.
            if (NeedsPin(req.Discount) || requested.Any(i => NeedsPin(i.Discount)))
            {
                var pinResult = await _managerPin.VerifyAsync(tenantId, requestingUserId, req.ManagerPin);
                if (!pinResult.Authorized)
                    return (false, pinResult.Error ?? "A manager PIN is required to apply this discount or comp.", outcome);
                outcome.AuthorizedByUserId = pinResult.AuthorizedUserId;
                outcome.AuthorizedByName = pinResult.AuthorizedName;
            }

            // Cache member lookups so applying the same perk to several lines hits the DB once.
            var memberCache = new Dictionary<string, (User? user, bool pass, bool loam, SeasonPassBenefitGrant? fbGrant)>(
                StringComparer.OrdinalIgnoreCase);
            async Task<(User? user, bool pass, bool loam, SeasonPassBenefitGrant? fbGrant)> ResolveMemberCached(string? key)
            {
                var k = key?.Trim() ?? "";
                if (memberCache.TryGetValue(k, out var hit)) return hit;
                var res = await ResolveMemberAsync(k);
                memberCache[k] = res;
                return res;
            }

            // Resolve a single discount input against a base, returning the cents + display metadata or an error.
            async Task<(int cents, string kind, string label, Guid? compId, string? compLabel, string? error)> ResolveOne(
                ConcessionDiscountInput d, int baseCents)
            {
                switch (d.Kind)
                {
                    case "preset":
                        if (d.PresetId is null) return (0, "", "", null, null, "Discount preset missing.");
                        // Pre-loaded above; a miss means it isn't this tenant's.
                        if (!presetsById.TryGetValue(d.PresetId.Value, out var preset) || !preset.IsActive)
                            return (0, "", "", null, null, "That discount isn't available.");
                        // Surface check, not decoration: a discount scoped to the bike shop must
                        // not be applicable to a burger just because a cashier knows its id.
                        if (!preset.AppliesTo(Services.Repositories.Data.DiscountData.DiscountSurfaces.Concession))
                            return (0, "", "", null, null, $"\"{preset.Name}\" doesn't apply to food and drink.");
                        return (ComputeDiscountCents(preset.Kind, preset.Value, baseCents), "preset", preset.Name, null, null, null);
                    case "percent":
                        var pb = Math.Clamp(d.Percent ?? 0, 0, 10000);
                        return (ComputeDiscountCents("percent", pb, baseCents), "percent", $"{pb / 100.0:0.##}% off", null, null, null);
                    case "amount":
                        var amt = Math.Max(0, d.AmountCents ?? 0);
                        return (ComputeDiscountCents("amount", amt, baseCents), "amount", $"${amt / 100.0:0.00} off", null, null, null);
                    case "comp":
                        if (d.CompReasonId is null) return (0, "", "", null, null, "Comp reason missing.");
                        var reason = await _concessions.GetCompReason(d.CompReasonId.Value, tenantId);
                        if (reason is null || !reason.IsActive) return (0, "", "", null, null, "That comp reason isn't available.");
                        return (ComputeDiscountCents(reason.DefaultKind, reason.DefaultValue, baseCents), "comp", reason.Name, reason.Id, reason.Name, null);
                    case "season_pass":
                    case "loampass":
                        var (mUser, mPass, mLoam, mGrant) = await ResolveMemberCached(d.CustomerEmailOrPhone);
                        if (mUser is null) return (0, "", "", null, null, "No customer found for that email or phone.");
                        var isPass = d.Kind == "season_pass";

                        // The pass perk goes through the shared resolver, so this till, the shop
                        // register, the online store and both rental paths agree on precedence
                        // (per-pass beats tenant-wide, and per-pass survives the tenant switch being
                        // off because it is product configuration, not the loyalty scheme).
                        // mGrant/mPass above are the LOOKUP's view, used for the error wording.
                        var passPerk = isPass
                            ? await _perks.Resolve(mUser.Id, _tenantContext.Tenant, "concession", baseCents, DateTime.UtcNow)
                            : Services.Pricing.SeasonPassPerk.None;

                        var enabled = isPass
                            ? mGrant is not null || _tenantContext.Tenant.SeasonPassDiscountAppliesTo("concession")
                            : (settings?.LoampassDiscountEnabled ?? false);
                        if (!enabled) return (0, "", "", null, null, "That member discount isn't enabled.");

                        var eligible = isPass ? passPerk.Any || mGrant is not null : mLoam;
                        if (!eligible) return (0, "", "", null, null, isPass
                            ? "That customer doesn't have an active season pass."
                            : "That customer isn't a linked LoamPass holder.");
                        outcome.PurchaserUserId = mUser.Id;
                        outcome.PurchaserEmail = mUser.Email;
                        outcome.PurchaserName = $"{mUser.FirstName} {mUser.LastName}".Trim();

                        if (isPass)
                        {
                            // Label comes from the resolver, which names the pass for a per-pass
                            // perk so the receipt and the comp report say which one was given.
                            return (passPerk.DiscountCents, d.Kind, passPerk.Label, null, null, null);
                        }
                        // LoamPass stays an F&B menu setting: it is specific to the LoamMx integration.
                        return (ComputeDiscountCents(settings!.LoampassDiscountKind, settings.LoampassDiscountValue, baseCents),
                            d.Kind, "LoamPass discount", null, null, null);
                    default:
                        return (0, "", "", null, null, "Unknown discount type.");
                }
            }

            var kindsApplied = new HashSet<string>();
            string? orderLabel = null;

            // 1) Per-line discounts. Lines are 1:1 with the qty>0 requested items, in order.
            for (var i = 0; i < lines.Count && i < requested.Count; i++)
            {
                var d = requested[i].Discount;
                if (d is null || string.IsNullOrEmpty(d.Kind)) continue;
                var line = lines[i];
                var gross = line.LineTotalCents;
                var (cents, kind, label, compId, compLabel, err) = await ResolveOne(d, gross);
                if (err != null) return (false, err, outcome);
                if (cents <= 0) continue;
                line.DiscountCents += cents;
                line.LineTotalCents = gross - cents;
                line.DiscountKind = kind;
                line.DiscountLabel = label;
                line.TaxCents = ComputeLineTax(line.LineTotalCents, line.TaxRateBps, pricesIncludeTax);
                kindsApplied.Add(kind);
                if (compId != null) { outcome.CompReasonId = compId; outcome.CompReasonLabel = compLabel; }
            }

            // 2) Order-level discount, allocated across the (post-line-discount) net line totals.
            if (req.Discount != null && !string.IsNullOrEmpty(req.Discount.Kind))
            {
                var netSubtotal = lines.Sum(l => l.LineTotalCents);
                var (cents, kind, label, compId, compLabel, err) = await ResolveOne(req.Discount, netSubtotal);
                if (err != null) return (false, err, outcome);
                if (cents > 0)
                {
                    var shares = AllocateProportional(cents, lines.Select(l => l.LineTotalCents).ToList());
                    for (var i = 0; i < lines.Count; i++)
                    {
                        if (shares[i] <= 0) continue;
                        lines[i].DiscountCents += shares[i];
                        lines[i].LineTotalCents -= shares[i];
                        lines[i].TaxCents = ComputeLineTax(lines[i].LineTotalCents, lines[i].TaxRateBps, pricesIncludeTax);
                        if (string.IsNullOrEmpty(lines[i].DiscountKind)) { lines[i].DiscountKind = kind; lines[i].DiscountLabel = label; }
                    }
                    kindsApplied.Add(kind);
                    orderLabel = label;
                    if (compId != null) { outcome.CompReasonId = compId; outcome.CompReasonLabel = compLabel; }
                }
            }

            // 3) Summary: total taken off, plus a single kind/label (or 'mixed' when several kinds applied).
            outcome.DiscountCents = lines.Sum(l => l.DiscountCents);
            if (kindsApplied.Count == 1)
            {
                outcome.DiscountKind = kindsApplied.First();
                outcome.DiscountLabel = orderLabel ?? lines.FirstOrDefault(l => l.DiscountCents > 0 && !string.IsNullOrEmpty(l.DiscountLabel))?.DiscountLabel;
            }
            else if (kindsApplied.Count > 1)
            {
                outcome.DiscountKind = "mixed";
                outcome.DiscountLabel = "Discounts";
            }
            return (true, null, outcome);
        }

        // ── Online ordering hours (weekly, evaluated in the tenant's timezone) ──────
        // Normalize a submitted 7-day schedule to JSON, or null when no schedule (always open).
        private static string? SerializeOrderingHours(List<ConcessionOrderingHoursDay>? hours)
        {
            if (hours is null || hours.Count == 0) return null;
            var norm = new List<ConcessionOrderingHoursDay>();
            for (var i = 0; i < 7; i++)
            {
                var d = i < hours.Count ? hours[i] : new ConcessionOrderingHoursDay();
                var open = Math.Clamp(d.OpenMinute, 0, 1440);
                var close = Math.Clamp(d.CloseMinute, 0, 1440);
                norm.Add(new ConcessionOrderingHoursDay { Open = d.Open && close > open, OpenMinute = open, CloseMinute = close });
            }
            return System.Text.Json.JsonSerializer.Serialize(norm);
        }

        private static List<ConcessionOrderingHoursDay>? ParseOrderingHours(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return System.Text.Json.JsonSerializer.Deserialize<List<ConcessionOrderingHoursDay>>(json); }
            catch { return null; }
        }

        private TimeZoneInfo TenantTz()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(_tenantContext.Tenant.Timezone ?? "UTC"); }
            catch { return TimeZoneInfo.Utc; }
        }

        // "Today" as a date in the tenant's timezone (for 86 / availability / validity-window checks). The
        // stored 86 sold_out_date is compared by .Date, so this keeps the 86 valid until LOCAL midnight.
        private DateTime TenantToday() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TenantTz()).Date;

        // Start of "today" in the tenant's timezone, as a UTC instant (for daily stats windows).
        private DateTime TenantTodayStartUtc()
        {
            var tz = TenantTz();
            var localMidnight = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).Date;
            return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localMidnight, DateTimeKind.Unspecified), tz);
        }

        // Is online ordering open right now? Null/empty schedule = always open. Evaluated in tenant tz.
        private bool IsOrderingOpen(List<ConcessionOrderingHoursDay>? hours)
        {
            if (hours is null || hours.Count < 7) return true;
            var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TenantTz());
            var day = hours[(int)localNow.DayOfWeek];   // DayOfWeek: Sunday = 0
            if (!day.Open) return false;
            var mins = localNow.Hour * 60 + localNow.Minute;
            return mins >= day.OpenMinute && mins < day.CloseMinute;
        }

        // ── Open season (date ranges) + event-day gating, layered on the weekly hours ───────────────
        // Drop invalid/empty ranges to JSON, or null when none remain (open year-round).
        private static string? SerializeOrderingSeasons(List<ConcessionOrderingSeason>? seasons)
        {
            var valid = (seasons ?? new())
                .Where(s => DateOnly.TryParse(s.StartDate, out _) && DateOnly.TryParse(s.EndDate, out _))
                // Normalize so start <= end and store the canonical "yyyy-MM-dd" form.
                .Select(s =>
                {
                    var a = DateOnly.Parse(s.StartDate);
                    var b = DateOnly.Parse(s.EndDate);
                    if (a > b) (a, b) = (b, a);
                    return new ConcessionOrderingSeason { StartDate = a.ToString("yyyy-MM-dd"), EndDate = b.ToString("yyyy-MM-dd") };
                })
                .ToList();
            return valid.Count == 0 ? null : System.Text.Json.JsonSerializer.Serialize(valid);
        }

        private static List<ConcessionOrderingSeason>? ParseOrderingSeasons(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return System.Text.Json.JsonSerializer.Deserialize<List<ConcessionOrderingSeason>>(json); }
            catch { return null; }
        }

        // Today (tenant tz) falls inside an open-season range. No ranges set = open year-round.
        private bool IsInOpenSeason(List<ConcessionOrderingSeason>? seasons)
        {
            if (seasons is null || seasons.Count == 0) return true;
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TenantTz()));
            return seasons.Any(s =>
                DateOnly.TryParse(s.StartDate, out var start) && DateOnly.TryParse(s.EndDate, out var end)
                && today >= start && today <= end);
        }

        // Is there a non-cancelled event on the calendar for today (tenant tz)? Used to default F&B
        // closed on days the track isn't running.
        private async Task<bool> HasEventToday()
        {
            var tz = TenantTz();
            var localToday = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).Date;
            var startUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localToday, DateTimeKind.Unspecified), tz);
            var endUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localToday.AddDays(1), DateTimeKind.Unspecified), tz);
            var events = await _events.GetInRange(_tenantContext.TenantId, startUtc, endUtc);
            return events.Any(e => !string.Equals(e.Status, "cancelled", StringComparison.OrdinalIgnoreCase));
        }

        // The full online-ordering gate: in open season AND (an event is on today OR event-day gating is
        // off) AND within today's weekly hours. Null settings = no customization, so defaults apply
        // (event-day gating on, no hours/season limits). Evaluated in the tenant's timezone.
        private async Task<bool> IsOnlineOrderingOpenNow(ConcessionMenuSettings? settings)
        {
            if (!IsInOpenSeason(ParseOrderingSeasons(settings?.OrderingSeasonsJson))) return false;
            if ((settings?.RequireEventDay ?? true) && !await HasEventToday()) return false;
            return IsOrderingOpen(ParseOrderingHours(settings?.OrderingHoursJson));
        }

        // ── Online-order throttle + live quote ───────────────────────────────────────
        // Layers the capacity gate (manual pause, active-order cap) and a live quote time on top of the
        // base season/event/hours gate. Used by the rider status poll, the rider order endpoint, and the
        // "Order Food" link visibility (MenuSettings.OrderingOpenNow).
        private async Task<ConcessionOrderingStatusResponse> EvaluateOrderingStatus(ConcessionMenuSettings? settings)
        {
            var tenantId = _tenantContext.TenantId;
            var baseOpen = await IsOnlineOrderingOpenNow(settings);
            var cap = await _concessions.GetOrderingCapacity(tenantId);

            // No capacity config or feature off: just the base gate, no quote.
            if (cap is null || !cap.CapacityEnabled)
                return new ConcessionOrderingStatusResponse { OpenNow = baseOpen, Reason = baseOpen ? null : ClosedGateReason };

            if (!baseOpen)
                return new ConcessionOrderingStatusResponse { OpenNow = false, CapacityEnabled = true, Reason = ClosedGateReason };
            if (cap.OnlinePaused)
                return new ConcessionOrderingStatusResponse { OpenNow = false, CapacityEnabled = true, PausedManual = true,
                    Reason = "Online ordering is paused right now. Please order at the window." };

            var activeOrders = await _concessions.CountActiveOrders(tenantId);
            if (cap.MaxActiveOrders > 0 && activeOrders >= cap.MaxActiveOrders)
                return new ConcessionOrderingStatusResponse { OpenNow = false, CapacityEnabled = true, CapReached = true,
                    Reason = "The kitchen is at capacity. Please order at the window or check back in a few minutes." };

            int? quote = cap.ShowQuoteTimes ? await ComputeQuoteMinutes(tenantId, cap.BasePrepMinutes) : null;
            return new ConcessionOrderingStatusResponse { OpenNow = true, CapacityEnabled = true, QuoteMinutes = quote };
        }

        private const string ClosedGateReason = "Online ordering is closed right now. Please check back when the track is open.";

        // Live ready-time estimate: base prep + the current backlog spread across the cooking stations,
        // using today's measured average prep time (falls back to ~2 min/item with no data). Rounded up
        // to the nearest 5 minutes for a friendly number.
        private async Task<int> ComputeQuoteMinutes(Guid tenantId, int baseMinutes)
        {
            var ahead = await _concessions.CountActivePrepLines(tenantId);
            var stats = await _concessions.GetKitchenStats(tenantId, TenantTodayStartUtc());
            var lanes = Math.Max(1, (await _concessions.ListStations(tenantId, activeOnly: true)).Count);
            var avgMin = Math.Clamp(stats.AvgPrepSeconds > 0 ? stats.AvgPrepSeconds / 60.0 : 2.0, 0.5, 15.0);
            var congestion = (int)Math.Ceiling(ahead * avgMin / lanes);
            var quote = baseMinutes + congestion;
            return (int)(Math.Ceiling(quote / 5.0) * 5);   // round up to nearest 5
        }

        // Live online-ordering status for the rider app (poll). Readable by any authenticated tenant user.
        // Anonymous for the same reason as GET Menu: open/closed + wait quote, no PII.
        [HttpGet("OrderingStatus")]
        public async Task<IActionResult> OrderingStatus()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var settings = await _concessions.GetMenuSettings(_tenantContext.TenantId);
            return new ApiResponses().OkResult(await EvaluateOrderingStatus(settings));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpGet("OrderingCapacity")]
        public async Task<IActionResult> GetOrderingCapacity()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var c = await _concessions.GetOrderingCapacity(_tenantContext.TenantId);
            return new ApiResponses().OkResult(new ConcessionOrderingCapacityResponse
            {
                CapacityEnabled = c?.CapacityEnabled ?? false,
                BasePrepMinutes = c?.BasePrepMinutes ?? 10,
                MaxActiveOrders = c?.MaxActiveOrders ?? 0,
                ShowQuoteTimes = c?.ShowQuoteTimes ?? true,
                OnlinePaused = c?.OnlinePaused ?? false,
            });
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPut("OrderingCapacity")]
        public async Task<IActionResult> UpdateOrderingCapacity([FromBody] ConcessionOrderingCapacityRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            await _concessions.UpsertOrderingCapacity(new ConcessionOrderingCapacity
            {
                TenantId = _tenantContext.TenantId,
                CapacityEnabled = req.CapacityEnabled,
                BasePrepMinutes = Math.Clamp(req.BasePrepMinutes, 0, 240),
                MaxActiveOrders = Math.Clamp(req.MaxActiveOrders, 0, 1000),
                ShowQuoteTimes = req.ShowQuoteTimes,
            });
            return new ApiResponses().OkResult();
        }

        // Manual online-ordering pause/resume from the cook or cashier screen.
        [Authorize(Policy = TenantPermissions.Policy.ConcessionsCounter)]
        [HttpPost("Ordering/Pause")]
        public async Task<IActionResult> SetOnlinePaused([FromBody] ConcessionPauseRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            await _concessions.SetOnlinePaused(_tenantContext.TenantId, req.Paused);
            return new ApiResponses().OkResult(new { paused = req.Paused });
        }

        // Pickup number board for an in-venue display: live order numbers grouped into ready vs preparing.
        // Reuses the kitchen feed (paid, not-yet-completed sales), so it tracks the cook screen exactly.
        [Authorize]
        [HttpGet("Board")]
        public async Task<IActionResult> PickupBoard()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var sales = await _concessions.GetKitchenSales(_tenantContext.TenantId);
            static ConcessionBoardResponse.Entry Map(ConcessionSale s) => new()
            {
                OrderNumber = s.OrderNumber,
                CustomerName = string.IsNullOrWhiteSpace(s.PurchaserName) ? null : s.PurchaserName,
            };
            // Tabs are table/bar service: their rounds are run out by staff, so they never appear on
            // the walk-up "your number is ready" board (an open tab would otherwise sit there all night).
            var board = new ConcessionBoardResponse
            {
                Ready = sales.Where(s => !s.IsTab && s.FulfillmentStatus == "ready" && s.OrderNumber.HasValue)
                             .OrderBy(s => s.OrderNumber).Select(Map).ToList(),
                Preparing = sales.Where(s => !s.IsTab && s.FulfillmentStatus == "active" && s.OrderNumber.HasValue)
                                 .OrderBy(s => s.OrderNumber).Select(Map).ToList(),
            };
            return new ApiResponses().OkResult(board);
        }

        // ── Inventory items ──────────────────────────────────────────────────────
        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpGet("Inventory/Items")]
        public async Task<IActionResult> ListInventoryItems()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var items = await _concessions.ListInventoryItems(_tenantContext.TenantId, activeOnly: false);
            return new ApiResponses().OkResult(items.Select(ToInventoryItemResponse).ToList());
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost("Inventory/Items")]
        public async Task<IActionResult> CreateInventoryItem([FromBody] ConcessionInventoryItemRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var item = new ConcessionInventoryItem
            {
                TenantId = _tenantContext.TenantId,
                Name = req.Name.Trim(),
                Unit = Blank(req.Unit) ?? "each",
                CostCents = req.CostCents,
                OnHand = req.OnHand,
                LowStockThreshold = req.LowStockThreshold,
                IsActive = req.IsActive,
            };
            item.Id = await _concessions.CreateInventoryItem(item);
            return new ApiResponses().OkResult(ToInventoryItemResponse(item));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPut("Inventory/Items/{id:guid}")]
        public async Task<IActionResult> UpdateInventoryItem(Guid id, [FromBody] ConcessionInventoryItemRequest req)
        {
            var existing = await _concessions.GetInventoryItem(id, _tenantContext.TenantId);
            if (existing is null) return new ApiResponses().NotFoundResult("Inventory item not found.");
            existing.Name = req.Name.Trim();
            existing.Unit = Blank(req.Unit) ?? "each";
            existing.CostCents = req.CostCents;
            existing.OnHand = req.OnHand;
            existing.LowStockThreshold = req.LowStockThreshold;
            existing.IsActive = req.IsActive;
            await _concessions.UpdateInventoryItem(existing);
            return new ApiResponses().OkResult(ToInventoryItemResponse(existing));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpDelete("Inventory/Items/{id:guid}")]
        public async Task<IActionResult> DeleteInventoryItem(Guid id)
        {
            await _concessions.DeleteInventoryItem(id, _tenantContext.TenantId);
            return new ApiResponses().OkResult();
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost("Inventory/Items/{id:guid}/Receive")]
        public async Task<IActionResult> ReceiveStock(Guid id, [FromBody] ConcessionReceiveStockRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var item = await _concessions.GetInventoryItem(id, _tenantContext.TenantId);
            if (item is null) return new ApiResponses().NotFoundResult("Inventory item not found.");
            // Receiving is additive only; a negative quantity would silently deplete on-hand.
            if (req.Quantity <= 0) return new ApiResponses().BadRequestResult("Enter a quantity greater than zero.");
            await _concessions.ReceiveStock(id, _tenantContext.TenantId, req.Quantity);
            return new ApiResponses().OkResult();
        }

        // ── Recipes (per product) ─────────────────────────────────────────────────
        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpGet("Products/{id:guid}/Recipe")]
        public async Task<IActionResult> GetRecipe(Guid id)
        {
            var product = await _concessions.GetProduct(id, _tenantContext.TenantId);
            if (product is null) return new ApiResponses().NotFoundResult("Item not found.");
            var lines = await _concessions.GetRecipe(id);
            return new ApiResponses().OkResult(lines.Select(l => new ConcessionRecipeLineResponse
            {
                InventoryItemId = l.InventoryItemId,
                ItemName = l.ItemName ?? "",
                Unit = l.Unit ?? "each",
                Quantity = l.Quantity,
            }).ToList());
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPut("Products/{id:guid}/Recipe")]
        public async Task<IActionResult> SetRecipe(Guid id, [FromBody] ConcessionRecipeRequest req)
        {
            var product = await _concessions.GetProduct(id, _tenantContext.TenantId);
            if (product is null) return new ApiResponses().NotFoundResult("Item not found.");
            // Only this tenant's inventory items can be recipe ingredients.
            var validIds = (await _concessions.ListInventoryItems(_tenantContext.TenantId, activeOnly: false)).Select(i => i.Id).ToHashSet();
            var lines = req.Lines.Where(l => validIds.Contains(l.InventoryItemId) && l.Quantity > 0)
                .Select(l => (l.InventoryItemId, l.Quantity)).ToList();
            await _concessions.SetRecipe(id, lines);
            return new ApiResponses().OkResult();
        }

        // ── Combo definition (shared, tenant-level) ─────────────────────────────────
        // Read the "make it a combo" config (tiers + slots). Anonymous, matching the other
        // menu reads (GET Menu / OrderingStatus): the public order page and the embedded
        // F&B widget render the upgrade before anyone signs in. Catalog data only.
        [HttpGet("Combo")]
        public async Task<IActionResult> GetCombo()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            return new ApiResponses().OkResult(await BuildComboConfig(_tenantContext.TenantId));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPut("Combo")]
        public async Task<IActionResult> SetCombo([FromBody] ConcessionComboConfigRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var tenantId = _tenantContext.TenantId;

            var tiers = req.Tiers
                .Where(t => !string.IsNullOrWhiteSpace(t.Name))
                .Select(t => new ConcessionComboTier
                {
                    Name = t.Name.Trim(),
                    SizeLabel = string.IsNullOrWhiteSpace(t.SizeLabel) ? null : t.SizeLabel.Trim(),
                    PriceCents = Math.Max(0, t.PriceCents),
                }).ToList();

            // Only this tenant's own products can be combo components.
            var ownProductIds = (await _concessions.ListProducts(tenantId, activeOnly: false)).Select(p => p.Id).ToHashSet();
            var slots = new List<ConcessionComboSlot>();
            foreach (var s in req.Slots)
            {
                var name = (s.Name ?? "").Trim();
                if (name.Length == 0) continue;
                var slot = new ConcessionComboSlot { Name = name, IsRequired = s.IsRequired, Options = new() };
                foreach (var o in s.Options)
                {
                    if (!ownProductIds.Contains(o.ComponentProductId)) continue;   // foreign/unknown component
                    slot.Options.Add(new ConcessionComboSlotOption
                    {
                        ComponentProductId = o.ComponentProductId,
                        IsDefault = o.IsDefault,
                    });
                }
                slots.Add(slot);
            }

            await _concessions.SetComboTiers(tenantId, tiers);
            await _concessions.SetComboSlots(tenantId, slots);
            return new ApiResponses().OkResult();
        }

        // ── Stock takes ────────────────────────────────────────────────────────────
        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost("Inventory/Counts")]
        public async Task<IActionResult> CreateInventoryCount([FromBody] ConcessionInventoryCountRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            Guid? countedBy = Guid.TryParse(User.FindFirst("UserId")?.Value, out var uid) ? uid : null;
            var lines = req.Lines.Select(l => (l.InventoryItemId, l.CountedQty)).ToList();
            var countId = await _concessions.CreateInventoryCount(_tenantContext.TenantId, countedBy, Blank(req.Note), lines);
            return new ApiResponses().OkResult(new { id = countId });
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpGet("Inventory/Counts")]
        public async Task<IActionResult> ListInventoryCounts()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var counts = await _concessions.ListInventoryCounts(_tenantContext.TenantId);
            return new ApiResponses().OkResult(counts.Select(c => new ConcessionInventoryCountSummary
            {
                Id = c.Id,
                CreatedAtUtc = DateTime.SpecifyKind(c.CreatedAt, DateTimeKind.Utc),
                Note = c.Note,
                VarianceCents = c.VarianceCents,
            }).ToList());
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpGet("Inventory/Counts/{id:guid}")]
        public async Task<IActionResult> GetInventoryCount(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var lines = await _concessions.GetInventoryCountLines(id, _tenantContext.TenantId);
            if (lines.Count == 0) return new ApiResponses().NotFoundResult("Count not found.");
            var detailLines = lines.Select(l => new ConcessionInventoryCountDetail.Line
            {
                Name = l.NameSnapshot,
                Unit = l.UnitSnapshot,
                ExpectedQty = l.ExpectedQty,
                CountedQty = l.CountedQty,
                Variance = l.CountedQty - l.ExpectedQty,
                UnitCostCents = l.UnitCostCents,
                VarianceCents = (long)Math.Round((l.CountedQty - l.ExpectedQty) * l.UnitCostCents),
            }).ToList();
            return new ApiResponses().OkResult(new ConcessionInventoryCountDetail
            {
                Id = id,
                Lines = detailLines,
                TotalVarianceCents = detailLines.Sum(l => l.VarianceCents),
            });
        }

        private static ConcessionInventoryItemResponse ToInventoryItemResponse(ConcessionInventoryItem i) => new()
        {
            Id = i.Id,
            Name = i.Name,
            Unit = i.Unit,
            CostCents = i.CostCents,
            OnHand = i.OnHand,
            LowStockThreshold = i.LowStockThreshold,
            IsLow = i.LowStockThreshold.HasValue && i.OnHand <= i.LowStockThreshold.Value,
            IsActive = i.IsActive,
        };

        // ── Admin: modifier groups + options ────────────────────────────────────
        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpGet("ModifierGroups")]
        public async Task<IActionResult> ListModifierGroupsAdmin()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var groups = await _concessions.ListModifierGroups(_tenantContext.TenantId, activeOnly: false);
            var optByGroup = (await _concessions.ListOptionsForGroups(groups.Select(g => g.Id), activeOnly: false))
                .GroupBy(o => o.GroupId).ToDictionary(g => g.Key, g => g.ToList());
            return new ApiResponses().OkResult(
                groups.Select(g => ToGroupResponse(g, optByGroup.GetValueOrDefault(g.Id, new()))).ToList());
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost("ModifierGroups")]
        public async Task<IActionResult> CreateModifierGroup([FromBody] ConcessionModifierGroupRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (ValidateModifierGroup(req) is { } gErr) return new ApiResponses().BadRequestResult(gErr);
            var g = new ConcessionModifierGroup
            {
                TenantId = _tenantContext.TenantId,
                Name = req.Name.Trim(),
                MinSelect = req.MinSelect,
                MaxSelect = req.MaxSelect,
                IsRequired = req.IsRequired,
                SortOrder = req.SortOrder,
                IsActive = req.IsActive,
            };
            g.Id = await _concessions.CreateModifierGroup(g);
            return new ApiResponses().OkResult(ToGroupResponse(g, new()));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPut("ModifierGroups/{id:guid}")]
        public async Task<IActionResult> UpdateModifierGroup(Guid id, [FromBody] ConcessionModifierGroupRequest req)
        {
            var existing = await _concessions.GetModifierGroup(id, _tenantContext.TenantId);
            if (existing is null) return new ApiResponses().NotFoundResult("Modifier group not found.");
            if (ValidateModifierGroup(req) is { } gErr) return new ApiResponses().BadRequestResult(gErr);
            existing.Name = req.Name.Trim();
            existing.MinSelect = req.MinSelect;
            existing.MaxSelect = req.MaxSelect;
            existing.IsRequired = req.IsRequired;
            existing.SortOrder = req.SortOrder;
            existing.IsActive = req.IsActive;
            await _concessions.UpdateModifierGroup(existing);
            var opts = await _concessions.ListOptionsForGroups(new[] { existing.Id }, activeOnly: false);
            return new ApiResponses().OkResult(ToGroupResponse(existing, opts));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpDelete("ModifierGroups/{id:guid}")]
        public async Task<IActionResult> DeleteModifierGroup(Guid id)
        {
            await _concessions.DeleteModifierGroup(id, _tenantContext.TenantId);
            return new ApiResponses().OkResult();
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost("ModifierGroups/{groupId:guid}/Options")]
        public async Task<IActionResult> CreateOption(Guid groupId, [FromBody] ConcessionModifierOptionRequest req)
        {
            var group = await _concessions.GetModifierGroup(groupId, _tenantContext.TenantId);
            if (group is null) return new ApiResponses().NotFoundResult("Modifier group not found.");
            var o = new ConcessionModifierOption
            {
                GroupId = groupId,
                Name = req.Name.Trim(),
                PriceDeltaCents = req.PriceDeltaCents,
                SortOrder = req.SortOrder,
                IsActive = req.IsActive,
            };
            o.Id = await _concessions.CreateOption(o);
            return new ApiResponses().OkResult(ToOptionResponse(o));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPut("ModifierGroups/{groupId:guid}/Options/{optionId:guid}")]
        public async Task<IActionResult> UpdateOption(Guid groupId, Guid optionId, [FromBody] ConcessionModifierOptionRequest req)
        {
            var group = await _concessions.GetModifierGroup(groupId, _tenantContext.TenantId);
            if (group is null) return new ApiResponses().NotFoundResult("Modifier group not found.");
            var existing = await _concessions.GetOption(optionId);
            if (existing is null || existing.GroupId != groupId)
                return new ApiResponses().NotFoundResult("Option not found.");
            existing.Name = req.Name.Trim();
            existing.PriceDeltaCents = req.PriceDeltaCents;
            existing.SortOrder = req.SortOrder;
            existing.IsActive = req.IsActive;
            await _concessions.UpdateOption(existing);
            return new ApiResponses().OkResult(ToOptionResponse(existing));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpDelete("ModifierGroups/{groupId:guid}/Options/{optionId:guid}")]
        public async Task<IActionResult> DeleteOption(Guid groupId, Guid optionId)
        {
            var group = await _concessions.GetModifierGroup(groupId, _tenantContext.TenantId);
            if (group is null) return new ApiResponses().NotFoundResult("Modifier group not found.");
            var existing = await _concessions.GetOption(optionId);
            if (existing is null || existing.GroupId != groupId)
                return new ApiResponses().NotFoundResult("Option not found.");
            await _concessions.DeleteOption(optionId);
            return new ApiResponses().OkResult();
        }

        // ── Cashier app (ConcessionsCounter) ────────────────────────────────────
        // Active items + variants for the cashier to ring up.
        [Authorize(Policy = TenantPermissions.Policy.ConcessionsCounter)]
        [HttpGet("Items")]
        public async Task<IActionResult> Items()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!_tenantContext.Tenant.ConcessionsEnabled)
                return new ApiResponses().OkResult(new List<ConcessionProductResponse>());
            var products = await _concessions.ListProducts(_tenantContext.TenantId, activeOnly: true);
            return new ApiResponses().OkResult(await Hydrate(products, activeOnly: true));
        }

        // Quick 86 / un-86 from the POS or cook screen (ConcessionsCounter, not admin). Marks the item sold out
        // for today only; it becomes available again automatically tomorrow.
        [Authorize(Policy = TenantPermissions.Policy.ConcessionsCounter)]
        [HttpPost("Products/{id:guid}/SoldOut")]
        public async Task<IActionResult> SetProductSoldOut(Guid id, [FromBody] ConcessionSoldOutRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var product = await _concessions.GetProduct(id, _tenantContext.TenantId);
            if (product is null) return new ApiResponses().NotFoundResult("Item not found.");
            await _concessions.SetProductSoldOut(
                id, _tenantContext.TenantId, req.SoldOut ? TenantToday() : (DateTime?)null);
            return new ApiResponses().OkResult(new { soldOut = req.SoldOut });
        }

        // Active stations for the cashier + cook screens (filter tabs). ConcessionsCounter, not admin.
        [Authorize(Policy = TenantPermissions.Policy.ConcessionsCounter)]
        [HttpGet("Stations/Active")]
        public async Task<IActionResult> ListActiveStations()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var stations = await _concessions.ListStations(_tenantContext.TenantId, activeOnly: true);
            return new ApiResponses().OkResult(stations.Select(ToStationResponse).ToList());
        }

        // Anonymous-buyer sale. Server computes the authoritative total from the catalog (never trusts
        // a client amount), applies modifiers + tip, and either takes cash (paid immediately, order
        // number now) or creates a card-present PaymentIntent the cashier confirms on the reader (the
        // payment webhook then flips it paid, assigns the order number, and writes the ledger entry).
        [Authorize(Policy = TenantPermissions.Policy.ConcessionsCounter)]
        [HttpPost("Sale")]
        public async Task<IActionResult> CreateSale([FromBody] ConcessionSaleRequest req, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!_tenantContext.Tenant.ConcessionsEnabled)
                return new ApiResponses().BadRequestResult("Food & Beverage isn't enabled for this track.");

            var tenantId = _tenantContext.TenantId;
            var paymentMethod = string.Equals(req.PaymentMethod?.Trim(), "cash", StringComparison.OrdinalIgnoreCase)
                ? "cash" : "card";
            // Tips are only honored when the tenant has enabled them; otherwise force 0 server-side.
            var menuSettings = await _concessions.GetMenuSettings(tenantId);
            var tipsEnabled = menuSettings?.TipsEnabled ?? false;
            var pricesIncludeTax = menuSettings?.PricesIncludeTax ?? false;
            var tipCents = tipsEnabled ? Math.Max(0, req.TipCents) : 0;

            var (lines, subtotal, cartError) = await ResolveCartLines(tenantId, req.Items);
            if (cartError is not null) return new ApiResponses().BadRequestResult(cartError);

            // The signed-in cashier: attributes the sale and is the subject of the manager-PIN lockout.
            Guid? soldBy = Guid.TryParse(User.FindFirst("UserId")?.Value, out var uid) ? uid : null;

            // Apply any discounts/comps server-side: this mutates the lines to NET totals + recomputed tax,
            // gates manual discounts/comps behind a manager PIN, and returns the summary to stamp on the sale.
            var requestedItems = req.Items.Where(i => i.Quantity > 0).ToList();
            var (discOk, discErr, disc) = await ApplyDiscounts(tenantId, soldBy ?? Guid.Empty, lines, requestedItems, req, menuSettings, pricesIncludeTax);
            if (!discOk) return new ApiResponses().BadRequestResult(discErr!);

            // Inclusive: tax is already inside the line totals (subtotal); exclusive: it's added on top.
            // SubtotalCents stays GROSS; the discount is subtracted to reach the total. Tax is on the net.
            var taxCents = lines.Sum(l => l.TaxCents);
            var total = subtotal - disc.DiscountCents + (pricesIncludeTax ? 0 : taxCents) + tipCents;
            var customerName = disc.PurchaserName ?? Blank(req.CustomerName);   // member name wins over a typed one

            // ── Store credit tender (Script0194): verify the account and cap at balance + total;
            // the money path (cash or card-present PI) collects only the remainder. The sale id is
            // pre-generated so the redeem entry can reference it BEFORE the row exists — a redeem
            // failure then aborts cleanly with nothing created.
            Services.Repositories.Data.CreditData.TenantCreditAccount? creditAccount = null;
            var creditApplied = 0;
            if (req.CreditAccountId is not null && req.CreditCents > 0 && total > 0)
            {
                creditAccount = await _credit.GetAccount(req.CreditAccountId.Value, tenantId);
                if (creditAccount is null)
                    return new ApiResponses().BadRequestResult("That store credit account no longer exists. Look it up again.");
                creditApplied = Math.Min(Math.Min(req.CreditCents, creditAccount.BalanceCents), Math.Max(0, total));
            }
            var due = total - creditApplied;
            var saleId = Guid.NewGuid();

            async Task<bool> RedeemCredit()
            {
                if (creditApplied <= 0) return true;
                return await _credit.TryAdjust(creditAccount!.Id, tenantId, -creditApplied, "redeem",
                    "concession_sale", saleId, null, soldBy);
            }

            // ── Paid immediately at the counter: cash, fully-comped $0, or credit covering it all.
            //    Order number now; ledger as 'cash' only when money actually changed hands. ─────────
            if (paymentMethod == "cash" || due <= 0)
            {
                if (subtotal <= 0) return new ApiResponses().BadRequestResult("Sale total must be greater than zero.");
                if (!await RedeemCredit())
                    return new ApiResponses().BadRequestResult(
                        "The store credit balance changed while ringing up. Look the customer up again.");
                var orderNumber = await _concessions.NextOrderNumber(tenantId);
                var cashSale = new ConcessionSale
                {
                    Id = saleId,
                    TenantId = tenantId,
                    Status = "paid",
                    FulfillmentStatus = "active",
                    OrderNumber = orderNumber,
                    SubtotalCents = subtotal,
                    TipCents = tipCents,
                    TaxCents = taxCents,
                    PricesIncludeTax = pricesIncludeTax,
                    DiscountCents = disc.DiscountCents,
                    DiscountKind = disc.DiscountKind,
                    DiscountLabel = disc.DiscountLabel,
                    CompReasonId = disc.CompReasonId,
                    CompReasonLabel = disc.CompReasonLabel,
                    AuthorizedByUserId = disc.AuthorizedByUserId,
                    AuthorizedByName = disc.AuthorizedByName,
                    TotalCents = Math.Max(0, total),
                    CreditAppliedCents = creditApplied,
                    CreditAccountId = creditApplied > 0 ? creditAccount!.Id : null,
                    PaymentMethod = "cash",
                    SoldByUserId = soldBy,
                    PurchaserUserId = disc.PurchaserUserId,
                    PurchaserEmail = disc.PurchaserEmail,
                    PurchaserName = customerName,
                    PaidAt = DateTime.UtcNow,
                };
                try
                {
                    cashSale.Id = await _concessions.CreateSale(cashSale);
                    await _concessions.CreateSaleLines(cashSale.Id, lines);
                }
                catch
                {
                    // The sale never landed; hand the credit straight back.
                    await _credit.ReverseRedeem(tenantId, "concession_sale", saleId, "sale could not be created");
                    throw;
                }
                // All grab-and-go? Every line is already 'ready', so settle the order as ready now:
                // nothing will ever hit the cook screen to bump it there.
                await _concessions.RecomputeSaleFulfillment(cashSale.Id, tenantId);
                if (cashSale.TotalCents - creditApplied > 0) await WriteCashLedger(cashSale);   // skip when no money changed hands
                try { await _concessions.DepleteInventoryForSale(cashSale.Id, tenantId); } catch { /* inventory is best-effort */ }
                await NotifyLowStock(tenantId);
                return new ApiResponses().OkResult(new ConcessionSaleResponse
                {
                    SaleId = cashSale.Id,
                    TotalCents = cashSale.TotalCents,
                    DiscountCents = disc.DiscountCents,
                    CreditAppliedCents = creditApplied,
                    DueCents = Math.Max(0, due),
                    Status = "paid",
                    OrderNumber = orderNumber,
                });
            }

            // ── Card-present (WisePOS E / Terminal). Order number assigned by the finalizer on paid. ─
            if (due < 50) return new ApiResponses().BadRequestResult(
                creditApplied > 0 ? "Less than 50 cents is due after credit. Take cash for the remainder."
                                  : "Card sale total must be at least 50 cents.");

            // Direct mode: the Location + card-present charge run on the tenant's connected account.
            // Concessions are all-in priced (no rider service charge), so there is no application fee.
            if (_tenantContext.Tenant.StripeChargeMode == "direct" && string.IsNullOrEmpty(_tenantContext.Tenant.StripeConnectAccountId))
                return new ApiResponses().BadRequestResult(
                    "This track is set to charge on its own Stripe account but hasn't connected one yet.");
            var connectedAccountId = DirectConnectedAccountId();
            var locationId = await EnsureTerminalLocation(connectedAccountId, ct);
            if (locationId is null)
                return new ApiResponses().BadRequestResult(
                    "Cannot take card-present payments until the track's address is filled in (Settings -> General).");

            if (!await RedeemCredit())
                return new ApiResponses().BadRequestResult(
                    "The store credit balance changed while ringing up. Look the customer up again.");

            var sale = new ConcessionSale
            {
                Id = saleId,
                TenantId = tenantId,
                Status = "pending",
                FulfillmentStatus = "active",
                SubtotalCents = subtotal,
                TipCents = tipCents,
                TaxCents = taxCents,
                PricesIncludeTax = pricesIncludeTax,
                DiscountCents = disc.DiscountCents,
                DiscountKind = disc.DiscountKind,
                DiscountLabel = disc.DiscountLabel,
                CompReasonId = disc.CompReasonId,
                CompReasonLabel = disc.CompReasonLabel,
                AuthorizedByUserId = disc.AuthorizedByUserId,
                AuthorizedByName = disc.AuthorizedByName,
                TotalCents = total,
                CreditAppliedCents = creditApplied,
                CreditAccountId = creditApplied > 0 ? creditAccount!.Id : null,
                PaymentMethod = string.IsNullOrEmpty(connectedAccountId) ? "stripe" : "stripe_direct",
                StripeConnectedAccountId = connectedAccountId,
                SoldByUserId = soldBy,
                PurchaserUserId = disc.PurchaserUserId,
                PurchaserEmail = disc.PurchaserEmail,
                PurchaserName = customerName,
            };
            try
            {
                sale.Id = await _concessions.CreateSale(sale);
                await _concessions.CreateSaleLines(sale.Id, lines);
            }
            catch
            {
                await _credit.ReverseRedeem(tenantId, "concession_sale", saleId, "sale could not be created");
                throw;
            }
            // NB: fulfillment stays 'active' until the card payment lands; StripePurchaseFinalizer
            // recomputes it on the paid flip so an all-grab-and-go order settles straight to 'ready'.

            var metadata = new Dictionary<string, string>
            {
                ["tenant_id"] = tenantId.ToString(),
                ["sale_kind"] = "concession",
                ["concession_sale_id"] = sale.Id.ToString(),
            };
            PaymentIntentCreated intent;
            try
            {
                intent = await _payments.CreateCardPresentPaymentIntentAsync(due, "usd", locationId, metadata, null,
                    connectedAccountId: connectedAccountId, ct: ct);
            }
            catch (InvalidOperationException ex)
            {
                await _concessions.MarkSaleFailed(sale.Id);
                await _credit.ReverseRedeem(tenantId, "concession_sale", sale.Id, "payment could not start");
                return new ApiResponses().BadRequestResult(ex.Message);
            }
            await _concessions.SetSalePaymentIntentId(sale.Id, intent.IntentId);

            return new ApiResponses().OkResult(new ConcessionSaleResponse
            {
                SaleId = sale.Id,
                ClientSecret = intent.ClientSecret,
                PaymentIntentId = intent.IntentId,
                TotalCents = total,
                DiscountCents = disc.DiscountCents,
                CreditAppliedCents = creditApplied,
                DueCents = due,
                Status = "pending",
            });
        }

        // Deliver a receipt for a sale to the customer's phone (text) or email. The customer picks the
        // method + enters the destination on the POS confirmation screen; print is handled client-side
        // via the ePOS printer, so only sms/email come through here.
        [Authorize(Policy = TenantPermissions.Policy.ConcessionsCounter)]
        [HttpPost("Sale/{id:guid}/Receipt")]
        public async Task<IActionResult> SendReceipt(Guid id, [FromBody] ConcessionReceiptRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var sale = await _concessions.GetSale(id, _tenantContext.TenantId);
            if (sale is null) return new ApiResponses().NotFoundResult("Sale not found.");
            var dest = Blank(req.Destination);
            if (dest is null) return new ApiResponses().BadRequestResult("Enter where to send the receipt.");

            var lines = await _concessions.GetSaleLines(sale.Id);
            var tenant = _tenantContext.Tenant;

            if (string.Equals(req.Channel, "sms", StringComparison.OrdinalIgnoreCase))
            {
                if (!_sms.IsConfiguredFor(tenant))
                    return new ApiResponses().BadRequestResult("Text receipts aren't set up for this track.");
                if (!await _sms.Send(tenant, dest, BuildReceiptText(tenant.DisplayName, sale, lines)))
                    return new ApiResponses().BadRequestResult("Could not send the text receipt.");
            }
            else
            {
                if (!_emailer.IsConfigured)
                    return new ApiResponses().BadRequestResult("Email receipts aren't set up.");
                var subject = $"{tenant.DisplayName} receipt — Order #{sale.OrderNumber}";
                if (!await _emailer.Send(dest, subject, BuildReceiptHtml(tenant.DisplayName, sale, lines), null, Services.Email.TenantEmailIdentity.For(tenant)))
                    return new ApiResponses().BadRequestResult("Could not send the email receipt.");
            }
            return new ApiResponses().OkResult();
        }

        private static string ReceiptMoney(int cents) => "$" + (cents / 100m).ToString("0.00");

        private static string BuildReceiptText(string header, ConcessionSale sale, List<ConcessionSaleLine> lines)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(header);
            sb.AppendLine($"Order #{sale.OrderNumber}");
            foreach (var l in lines)
            {
                var name = !string.IsNullOrWhiteSpace(l.VariantLabel) ? $"{l.NameSnapshot} ({l.VariantLabel})" : l.NameSnapshot;
                sb.AppendLine($"{l.Quantity}x {name}  {ReceiptMoney(l.LineTotalCents)}");
                if (!string.IsNullOrWhiteSpace(l.Notes)) sb.AppendLine($"  \"{l.Notes}\"");
            }
            // Inclusive: line totals already contain tax, so show the pre-tax subtotal + "tax included".
            var subtotalLabel = sale.PricesIncludeTax ? sale.SubtotalCents - sale.TaxCents : sale.SubtotalCents;
            sb.AppendLine($"Subtotal {ReceiptMoney(subtotalLabel)}");
            if (sale.DiscountCents > 0)
                sb.AppendLine($"{(string.IsNullOrWhiteSpace(sale.DiscountLabel) ? "Discount" : sale.DiscountLabel)} -{ReceiptMoney(sale.DiscountCents)}");
            if (sale.TaxCents > 0)
                sb.AppendLine($"Tax{(sale.PricesIncludeTax ? " (incl.)" : "")} {ReceiptMoney(sale.TaxCents)}");
            if (sale.TipCents > 0) sb.AppendLine($"Tip {ReceiptMoney(sale.TipCents)}");
            sb.AppendLine($"Total {ReceiptMoney(sale.TotalCents)}");
            return sb.ToString();
        }

        private static string BuildReceiptHtml(string header, ConcessionSale sale, List<ConcessionSaleLine> lines)
        {
            string Enc(string s) => System.Net.WebUtility.HtmlEncode(s);
            var sb = new System.Text.StringBuilder();
            sb.Append("<div style=\"font-family:Arial,Helvetica,sans-serif;max-width:380px\">");
            sb.Append($"<h2 style=\"margin:0 0 4px\">{Enc(header)}</h2>");
            sb.Append($"<p style=\"font-size:18px;font-weight:bold;margin:0 0 12px\">Order #{sale.OrderNumber}</p>");
            sb.Append("<table style=\"width:100%;border-collapse:collapse;font-size:14px\">");
            foreach (var l in lines)
            {
                var name = !string.IsNullOrWhiteSpace(l.VariantLabel) ? $"{l.NameSnapshot} ({l.VariantLabel})" : l.NameSnapshot;
                sb.Append($"<tr><td style=\"padding:2px 0\">{l.Quantity}&times; {Enc(name)}</td><td style=\"text-align:right\">{ReceiptMoney(l.LineTotalCents)}</td></tr>");
                if (!string.IsNullOrWhiteSpace(l.Notes))
                    sb.Append($"<tr><td colspan=\"2\" style=\"font-style:italic;color:#666;padding-bottom:4px\">\"{Enc(l.Notes)}\"</td></tr>");
            }
            sb.Append("</table><hr style=\"border:none;border-top:1px solid #ddd;margin:10px 0\">");
            var subtotalLabel = sale.PricesIncludeTax ? sale.SubtotalCents - sale.TaxCents : sale.SubtotalCents;
            sb.Append($"<div style=\"font-size:14px\">Subtotal: {ReceiptMoney(subtotalLabel)}<br>");
            if (sale.DiscountCents > 0)
                sb.Append($"{Enc(string.IsNullOrWhiteSpace(sale.DiscountLabel) ? "Discount" : sale.DiscountLabel)}: -{ReceiptMoney(sale.DiscountCents)}<br>");
            if (sale.TaxCents > 0)
                sb.Append($"Tax{(sale.PricesIncludeTax ? " (incl.)" : "")}: {ReceiptMoney(sale.TaxCents)}<br>");
            if (sale.TipCents > 0) sb.Append($"Tip: {ReceiptMoney(sale.TipCents)}<br>");
            sb.Append($"<strong style=\"font-size:16px\">Total: {ReceiptMoney(sale.TotalCents)}</strong></div></div>");
            return sb.ToString();
        }

        // ── Refund / void ───────────────────────────────────────────────────────
        // Reverses a paid sale: card via Stripe (on the connected account for direct sales), cash as
        // a recorded reversal. Writes a negative ledger entry so balances stay correct.
        [Authorize(Policy = TenantPermissions.Policy.ConcessionsCounter)]
        [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("manager-pin")]
        [HttpPost("Sale/{id:guid}/Refund")]
        public async Task<IActionResult> RefundSale(Guid id, [FromBody] ConcessionRefundRequest req, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!Guid.TryParse(User.FindFirst("UserId")?.Value, out var actingUserId))
                return new ApiResponses().BadRequestResult("Not signed in.");

            // A refund moves money and is a shrinkage vector, so it takes a manager PIN just like a comp.
            var pinResult = await _managerPin.VerifyAsync(_tenantContext.TenantId, actingUserId, req?.ManagerPin);
            if (!pinResult.Authorized)
                return new ApiResponses().BadRequestResult(pinResult.Error ?? "Manager authorization required to refund.");

            var sale = await _concessions.GetSale(id, _tenantContext.TenantId);
            if (sale is null) return new ApiResponses().NotFoundResult("Sale not found.");
            // Open tabs have taken no money yet and voided tabs never did, so neither can be refunded.
            if (ConcessionTabRules.RefundBlocker(sale.Status) is { } refundBlocker)
                return new ApiResponses().BadRequestResult(refundBlocker);

            if (sale.PaymentMethod is "stripe" or "stripe_direct" && !string.IsNullOrEmpty(sale.StripePaymentIntentId))
            {
                var isDirect = sale.PaymentMethod == "stripe_direct";
                try
                {
                    // The PI only charged the money portion; any store credit goes back to its
                    // account below.
                    await _payments.RefundAsync(sale.StripePaymentIntentId!, sale.TotalCents - sale.CreditAppliedCents,
                        idempotencyKey: $"refund-concession-{sale.Id}",
                        connectedAccountId: isDirect ? sale.StripeConnectedAccountId : null,
                        refundApplicationFee: isDirect, ct: ct);
                }
                catch (Exception ex)
                {
                    return new ApiResponses().BadRequestResult($"Refund failed at the payment processor: {ex.Message}");
                }
            }
            // cash: nothing to move at the processor; the reversal is recorded in the ledger below.

            await _concessions.MarkSaleRefunded(sale.Id, _tenantContext.TenantId);
            await _concessions.MarkSaleCompleted(sale.Id, _tenantContext.TenantId);
            if (sale.CreditAppliedCents > 0)
            {
                // A tab redeemed under its close attempt's own reference; a counter sale under the sale id.
                var (refKind, refId) = ConcessionTabCredit.ReferenceFor(sale);
                await _credit.ReverseRedeem(_tenantContext.TenantId, refKind, refId, "sale refunded");
            }
            await WriteRefundLedger(sale, actingUserId, pinResult.AuthorizedName);

            // The second of three refund paths in the app (the others are PurchaseController and
            // the bike shop register), so it needs its own audit entry to show up in the same
            // review. A cash concession refund is the purest form of till fraud available here:
            // no processor record, and the manager PIN that authorized it is the only other
            // control, so record who that was.
            await _audit.Log(
                "concession.refund",
                $"Refunded a ${sale.TotalCents / 100m:0.00} concession sale ({sale.PaymentMethod})",
                targetKind: "concession_sale",
                targetId: sale.Id,
                tenantId: _tenantContext.TenantId,
                metadata: new
                {
                    totalCents = sale.TotalCents,
                    creditAppliedCents = sale.CreditAppliedCents,
                    paymentMethod = sale.PaymentMethod,
                    stripePaymentIntentId = sale.StripePaymentIntentId,
                    // Who approved it via manager PIN, which may not be the person ringing it.
                    // The PIN itself is deliberately never recorded.
                    authorizedBy = pinResult.AuthorizedName,
                });

            return new ApiResponses().OkResult();
        }

        // ── Kitchen / cook screen ─────────────────────────────────────────────────
        [Authorize(Policy = TenantPermissions.Policy.ConcessionsCounter)]
        [HttpGet("Kitchen")]
        public async Task<IActionResult> Kitchen([FromQuery] Guid? stationId)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var sales = await _concessions.GetKitchenSales(_tenantContext.TenantId);
            var lines = await _concessions.GetKitchenLines(_tenantContext.TenantId, stationId);
            var linesBySale = lines.GroupBy(l => l.SaleId).ToDictionary(g => g.Key, g => g.ToList());

            // Per-product defaults so the cook screen can hide standard selections and surface only the
            // customizations (added items + removed defaults).
            var defaultsByProduct = await _concessions.ListProductDefaultOptionLinks(
                lines.Where(l => l.ProductId.HasValue).Select(l => l.ProductId!.Value).Distinct());
            var defaultNames = await _concessions.GetOptionNames(defaultsByProduct.Values.SelectMany(x => x).Distinct());

            var orders = new List<ConcessionKitchenResponse.KitchenOrder>();
            foreach (var s in sales)
            {
                if (!linesBySale.TryGetValue(s.Id, out var saleLines)) continue;   // no lines for this station
                // A plain order is one ticket. An open tab is one ticket PER ROUND: each round fired at
                // a different time and is cooked, aged and picked up on its own.
                var tickets = s.IsTab
                    ? saleLines.GroupBy(l => l.RoundNumber).OrderBy(g => g.Key).Select(g => ((int?)g.Key, g.ToList()))
                    : new[] { ((int?)null, saleLines) };
                foreach (var (roundNumber, ticketLines) in tickets)
                orders.Add(new ConcessionKitchenResponse.KitchenOrder
                {
                    SaleId = s.Id,
                    OrderNumber = s.OrderNumber,
                    // A tab round is 'ready' once its own lines are, independent of the other rounds.
                    FulfillmentStatus = s.IsTab
                        ? (ticketLines.All(l => l.PrepStatus == "ready") ? "ready" : "active")
                        : s.FulfillmentStatus,
                    CustomerName = string.IsNullOrWhiteSpace(s.PurchaserName) ? null : s.PurchaserName,
                    IsRush = s.IsRush,
                    IsTab = s.IsTab,
                    TabName = s.IsTab ? s.TabName : null,
                    RoundNumber = roundNumber,
                    // Anchor the age on when the order was paid (entered the kitchen); fall back to created.
                    // A tab round entered the kitchen when it fired.
                    QueuedAtUtc = DateTime.SpecifyKind(
                        s.IsTab ? ticketLines.Min(l => l.FiredAt) : (s.PaidAt ?? s.CreatedAt), DateTimeKind.Utc),
                    Lines = ticketLines.Select(l =>
                    {
                        var defIds = l.ProductId is Guid pid && defaultsByProduct.TryGetValue(pid, out var d)
                            ? new HashSet<Guid>(d) : new HashSet<Guid>();
                        var selectedIds = l.Modifiers.Where(m => m.ModifierOptionId.HasValue)
                            .Select(m => m.ModifierOptionId!.Value).ToHashSet();
                        return new ConcessionKitchenResponse.KitchenLine
                        {
                            LineId = l.Id,
                            StationId = l.StationId,
                            Name = l.NameSnapshot,
                            VariantLabel = l.VariantLabel,
                            Quantity = l.Quantity,
                            PrepStatus = l.PrepStatus,
                            Notes = l.Notes,
                            IsCombo = l.IsCombo,
                            ParentLineId = l.ParentLineId,
                            ComboTier = l.ComboTier,
                            // Chosen non-default options (e.g. "Bacon"); standard defaults are omitted.
                            Added = l.Modifiers.Where(m => !(m.ModifierOptionId.HasValue && defIds.Contains(m.ModifierOptionId.Value)))
                                .Select(m => m.OptionNameSnapshot).ToList(),
                            // Standard defaults the customer removed (e.g. "Lettuce").
                            Removed = defIds.Where(id => !selectedIds.Contains(id))
                                .Select(id => defaultNames.GetValueOrDefault(id, "item")).ToList(),
                            // Default options that stayed on (shown only when the cook toggles defaults on).
                            Standard = l.Modifiers.Where(m => m.ModifierOptionId.HasValue && defIds.Contains(m.ModifierOptionId.Value))
                                .Select(m => m.OptionNameSnapshot).ToList(),
                        };
                    }).ToList(),
                });
            }
            var stats = await _concessions.GetKitchenStats(_tenantContext.TenantId, TenantTodayStartUtc());
            var settings = await _concessions.GetMenuSettings(_tenantContext.TenantId);
            return new ApiResponses().OkResult(new ConcessionKitchenResponse
            {
                Orders = orders,
                WarnMinutes = settings?.PrepWarnMinutes ?? 5,
                LateMinutes = settings?.PrepLateMinutes ?? 10,
                Stats = new ConcessionKitchenResponse.KitchenStats
                {
                    CompletedToday = stats.Count,
                    AvgPrepMinutes = (int)Math.Round(stats.AvgPrepSeconds / 60.0),
                },
            });
        }

        // Advance one line's prep state (queued -> in_progress -> ready); recompute the order's
        // overall fulfillment so it flips to 'ready' once every line is ready.
        [Authorize(Policy = TenantPermissions.Policy.ConcessionsCounter)]
        [HttpPost("Kitchen/Line/{lineId:guid}/{prepStatus}")]
        public async Task<IActionResult> AdvanceLine(Guid lineId, string prepStatus)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (prepStatus is not ("queued" or "in_progress" or "ready"))
                return new ApiResponses().BadRequestResult("Invalid prep status.");
            var ok = await _concessions.AdvanceLinePrep(lineId, _tenantContext.TenantId, prepStatus);
            if (!ok) return new ApiResponses().NotFoundResult("Line not found.");
            // Resolve the sale so we can recompute its fulfillment; the line belongs to this tenant.
            var line = (await _concessions.GetKitchenLines(_tenantContext.TenantId, null))
                .FirstOrDefault(l => l.Id == lineId);
            if (line is not null)
            {
                await _concessions.RecomputeSaleFulfillment(line.SaleId, _tenantContext.TenantId);
                await NotifyIfReady(line.SaleId);
            }
            return new ApiResponses().OkResult();
        }

        // "All ready": bump every outstanding line on one ticket at once. One statement rather than a
        // client-side loop, so a ticket can never be left half-bumped by a call that failed midway.
        // ?round=N limits it to one tab round, since a tab can have several rounds on the board at once.
        [Authorize(Policy = TenantPermissions.Policy.ConcessionsCounter)]
        [HttpPost("Kitchen/Sale/{saleId:guid}/AllReady")]
        public async Task<IActionResult> MarkAllReady(Guid saleId, [FromQuery] int? round)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            await _concessions.MarkAllLinesReady(saleId, _tenantContext.TenantId, round);
            // Recompute unconditionally, not just when rows changed: an order whose lines were each
            // bumped individually is already all-ready and still needs its header flipped.
            await _concessions.RecomputeSaleFulfillment(saleId, _tenantContext.TenantId);
            await NotifyIfReady(saleId);
            return new ApiResponses().OkResult();
        }

        // When an online order just became fully ready, text the rider once ("your order is ready").
        // Best-effort: a notification failure must not fail the cook's bump.
        private async Task NotifyIfReady(Guid saleId)
        {
            try
            {
                var sale = await _concessions.GetSale(saleId, _tenantContext.TenantId);
                if (sale is null || sale.FulfillmentStatus != "ready"
                    || sale.OrderChannel != "online" || !sale.PurchaserUserId.HasValue)
                    return;
                // One-shot claim so we text exactly once across multiple line bumps.
                if (!await _concessions.TryMarkReadyNotified(sale.Id, _tenantContext.TenantId)) return;
                var user = await _users.GetById(sale.PurchaserUserId.Value);
                if (string.IsNullOrWhiteSpace(user?.Phone)) return;
                await _sms.Send(_tenantContext.Tenant, user.Phone,
                    $"Your {_tenantContext.Tenant.DisplayName} order #{sale.OrderNumber} is ready for pickup!");
            }
            catch { /* notification is best-effort; the bump already succeeded */ }
        }

        // Tab round picked up: its lines come off the cook screen while the tab stays open for more
        // rounds. Once a PAID tab has no unserved line left it completes like a normal order would.
        [Authorize(Policy = TenantPermissions.Policy.ConcessionsCounter)]
        [HttpPost("Kitchen/Sale/{saleId:guid}/Round/{round:int}/Served")]
        public async Task<IActionResult> MarkRoundServed(Guid saleId, int round)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var sale = await _concessions.GetSale(saleId, _tenantContext.TenantId);
            if (sale is null || !sale.IsTab) return new ApiResponses().NotFoundResult("Tab not found.");
            await _concessions.MarkRoundServed(saleId, _tenantContext.TenantId, round);
            await _concessions.RecomputeSaleFulfillment(saleId, _tenantContext.TenantId);
            await _concessions.CompleteTabIfAllServed(saleId, _tenantContext.TenantId);
            return new ApiResponses().OkResult();
        }

        // Mark an order picked up (drops it off the cook screen + open-orders list).
        [Authorize(Policy = TenantPermissions.Policy.ConcessionsCounter)]
        [HttpPost("Sale/{id:guid}/Complete")]
        public async Task<IActionResult> CompleteSale(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            await _concessions.MarkSaleCompleted(id, _tenantContext.TenantId);
            return new ApiResponses().OkResult();
        }

        // Recall: bring a just-completed order back onto the cook screen (mistake recovery).
        [Authorize(Policy = TenantPermissions.Policy.ConcessionsCounter)]
        [HttpPost("Sale/{id:guid}/Recall")]
        public async Task<IActionResult> RecallSale(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            await _concessions.RecallSale(id, _tenantContext.TenantId);
            return new ApiResponses().OkResult();
        }

        // Recently completed orders for the recall picker.
        [Authorize(Policy = TenantPermissions.Policy.ConcessionsCounter)]
        [HttpGet("Kitchen/Completed")]
        public async Task<IActionResult> RecentlyCompleted()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var recent = await _concessions.ListRecentlyCompleted(_tenantContext.TenantId);
            return new ApiResponses().OkResult(recent.Select(s => new
            {
                saleId = s.Id,
                orderNumber = s.OrderNumber,
                customerName = string.IsNullOrWhiteSpace(s.PurchaserName) ? null : s.PurchaserName,
            }).ToList());
        }

        // ── Order history (cashiers + cooks) ─────────────────────────────────────────
        // Searchable list of real orders (paid/refunded), newest first. q matches order number / name / email.
        [Authorize(Policy = TenantPermissions.Policy.ConcessionsCounter)]
        [HttpGet("Orders")]
        public async Task<IActionResult> ListOrders([FromQuery] string? q, [FromQuery] string? from, [FromQuery] string? to)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            // from/to are local calendar dates (yyyy-MM-dd) in the tenant's timezone; convert to a UTC
            // [start, nextDayStart) window so the day boundaries match the venue's clock.
            var tz = TenantTz();
            DateTime? fromUtc = DateTime.TryParse(from, out var fd)
                ? TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(fd.Date, DateTimeKind.Unspecified), tz) : null;
            DateTime? toUtc = DateTime.TryParse(to, out var td)
                ? TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(td.Date.AddDays(1), DateTimeKind.Unspecified), tz) : null;
            var sales = await _concessions.SearchSales(_tenantContext.TenantId, q, fromUtc, toUtc, 200);
            return new ApiResponses().OkResult(sales.Select(ToOrderSummary).ToList());
        }

        // One order with its line items (for the history detail view).
        [Authorize(Policy = TenantPermissions.Policy.ConcessionsCounter)]
        [HttpGet("Orders/{id:guid}")]
        public async Task<IActionResult> GetOrder(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var sale = await _concessions.GetSale(id, _tenantContext.TenantId);
            if (sale is null) return new ApiResponses().NotFoundResult("Order not found.");
            // A tab's detail is the superset (rounds grouped, hold card, open/closed stamps); a plain
            // order gets the flat detail as before.
            if (sale.IsTab) return new ApiResponses().OkResult(await BuildTabDetail(sale));
            var lines = await _concessions.GetSaleLines(sale.Id);
            var detail = FillOrderSummary(new ConcessionOrderDetail(), sale);
            detail.Lines = lines.Select(ToOrderLine).ToList();
            return new ApiResponses().OkResult(detail);
        }

        private static ConcessionOrderSummary ToOrderSummary(ConcessionSale s) => FillOrderSummary(new ConcessionOrderSummary(), s);

        // Shared by the list row, the order detail and the tab detail so the three never drift.
        private static T FillOrderSummary<T>(T d, ConcessionSale s) where T : ConcessionOrderSummary
        {
            d.SaleId = s.Id;
            d.OrderNumber = s.OrderNumber;
            d.Status = s.Status;
            d.FulfillmentStatus = s.FulfillmentStatus;
            d.PaymentMethod = s.PaymentMethod;
            d.OrderChannel = s.OrderChannel;
            d.CustomerName = string.IsNullOrWhiteSpace(s.PurchaserName) ? null : s.PurchaserName;
            d.SubtotalCents = s.SubtotalCents;
            d.TipCents = s.TipCents;
            d.TaxCents = s.TaxCents;
            d.PricesIncludeTax = s.PricesIncludeTax;
            d.DiscountCents = s.DiscountCents;
            d.DiscountKind = s.DiscountKind;
            d.DiscountLabel = s.DiscountLabel;
            d.AuthorizedByName = s.AuthorizedByName;
            d.TotalCents = s.TotalCents;
            d.IsRush = s.IsRush;
            d.CreatedAtUtc = DateTime.SpecifyKind(s.CreatedAt, DateTimeKind.Utc);
            d.PaidAtUtc = s.PaidAt.HasValue ? DateTime.SpecifyKind(s.PaidAt.Value, DateTimeKind.Utc) : null;
            d.TabName = s.TabName;
            d.VoidReason = s.VoidReason;
            d.VoidedAtUtc = s.VoidedAt.HasValue ? DateTime.SpecifyKind(s.VoidedAt.Value, DateTimeKind.Utc) : null;
            return d;
        }

        private static ConcessionOrderDetail.Line ToOrderLine(ConcessionSaleLine l) => new()
        {
            LineId = l.Id,
            Name = l.NameSnapshot,
            VariantLabel = l.VariantLabel,
            Quantity = l.Quantity,
            LineTotalCents = l.LineTotalCents,
            DiscountCents = l.DiscountCents,
            DiscountLabel = l.DiscountLabel,
            Notes = l.Notes,
            Modifiers = l.Modifiers.Select(m => m.OptionNameSnapshot).ToList(),
            IsCombo = l.IsCombo,
            ComboTier = l.ComboTier,
            ParentLineId = l.ParentLineId,
            RoundNumber = l.RoundNumber,
        };

        private async Task<ConcessionTabDetail> BuildTabDetail(ConcessionSale sale)
            => BuildTabDetail(sale, await _concessions.GetSaleLines(sale.Id));

        // Overload for callers that already hold the lines (AddTabRound answers from the rows it just wrote).
        private static ConcessionTabDetail BuildTabDetail(ConcessionSale sale, List<ConcessionSaleLine> lines)
        {
            var detail = FillOrderSummary(new ConcessionTabDetail(), sale);
            detail.Lines = lines.Select(ToOrderLine).ToList();
            detail.OpenedAtUtc = sale.TabOpenedAt.HasValue ? DateTime.SpecifyKind(sale.TabOpenedAt.Value, DateTimeKind.Utc) : null;
            detail.ClosedAtUtc = sale.TabClosedAt.HasValue ? DateTime.SpecifyKind(sale.TabClosedAt.Value, DateTimeKind.Utc) : null;
            detail.HoldCardLabel = ConcessionTabRules.HoldCardLabel(sale.HoldCardBrand, sale.HoldCardLast4);
            detail.HoldAmountCents = sale.HoldPaymentIntentId is null ? null : sale.HoldAmountCents;
            detail.Rounds = lines.GroupBy(l => l.RoundNumber).OrderBy(g => g.Key).Select(g => new ConcessionTabDetail.Round
            {
                RoundNumber = g.Key,
                FiredAtUtc = DateTime.SpecifyKind(g.Min(l => l.FiredAt), DateTimeKind.Utc),
                Lines = g.Select(ToOrderLine).ToList(),
            }).ToList();
            return detail;
        }

        private static ConcessionTabSummary ToTabSummary(ConcessionOpenTabRow r) => new()
        {
            SaleId = r.Id,
            OrderNumber = r.OrderNumber,
            TabName = r.TabName,
            CustomerName = string.IsNullOrWhiteSpace(r.PurchaserName) ? null : r.PurchaserName,
            OpenedAtUtc = DateTime.SpecifyKind(r.TabOpenedAt ?? r.CreatedAt, DateTimeKind.Utc),
            LastRoundAtUtc = r.LastRoundAt.HasValue ? DateTime.SpecifyKind(r.LastRoundAt.Value, DateTimeKind.Utc) : null,
            Rounds = r.Rounds,
            ItemCount = r.ItemCount,
            TotalCents = r.TotalCents,
            HoldCardLabel = r.HoldPaymentIntentId is null ? null : ConcessionTabRules.HoldCardLabel(r.HoldCardBrand, r.HoldCardLast4),
        };

        // Toggle the rush/priority flag on an order (cook screen sorts rush first + flags it).
        [Authorize(Policy = TenantPermissions.Policy.ConcessionsCounter)]
        [HttpPost("Sale/{id:guid}/Rush")]
        public async Task<IActionResult> SetRush(Guid id, [FromBody] ConcessionRushRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            await _concessions.SetRush(id, _tenantContext.TenantId, req.Rush);
            return new ApiResponses().OkResult();
        }

        // Sale status, polled by the POS after a card-present payment to pick up the order number the
        // webhook assigns on success (cash sales already have theirs).
        [Authorize(Policy = TenantPermissions.Policy.ConcessionsCounter)]
        [HttpGet("Sale/{id:guid}")]
        public async Task<IActionResult> GetSaleStatus(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var sale = await _concessions.GetSale(id, _tenantContext.TenantId);
            if (sale is null) return new ApiResponses().NotFoundResult("Sale not found.");
            return new ApiResponses().OkResult(new
            {
                status = sale.Status,
                orderNumber = sale.OrderNumber,
                totalCents = sale.TotalCents,
            });
        }

        // Finalize a card-present sale right after the reader confirms, instead of waiting on the Stripe
        // webhook (which can lag, or never reach a local/dev box). Verifies the PaymentIntent actually
        // succeeded at Stripe first, then runs the shared finalizer (idempotent with the webhook), so the
        // order number is assigned immediately. Returns the order number for the POS confirmation screen.
        [Authorize(Policy = TenantPermissions.Policy.ConcessionsCounter)]
        [HttpPost("Sale/{id:guid}/Finalize")]
        public async Task<IActionResult> FinalizeCardSale(Guid id, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var sale = await _concessions.GetSale(id, _tenantContext.TenantId);
            if (sale is null) return new ApiResponses().NotFoundResult("Sale not found.");
            if (sale.Status == "paid")
                return new ApiResponses().OkResult(new { status = sale.Status, orderNumber = sale.OrderNumber });
            if (string.IsNullOrEmpty(sale.StripePaymentIntentId))
                return new ApiResponses().BadRequestResult("This sale has no card payment to finalize.");

            var status = await _payments.GetPaymentIntentStatusAsync(sale.StripePaymentIntentId, sale.StripeConnectedAccountId, ct);
            if (status != "succeeded")
                return new ApiResponses().BadRequestResult("The card payment hasn't completed yet.");

            await _finalizer.ProcessPaymentIntentAsync(sale.StripePaymentIntentId, "payment_intent.succeeded", ct);
            var fresh = await _concessions.GetSale(id, _tenantContext.TenantId);
            return new ApiResponses().OkResult(new { status = fresh?.Status, orderNumber = fresh?.OrderNumber });
        }

        // ── Open tabs (Script0291) ────────────────────────────────────────────────────────────────
        // A tab is a concession_sale in status 'open': rung up in rounds (each round fires to the
        // kitchen and depletes inventory immediately), paid once at close through the same cash /
        // card-present / store-credit paths as a counter sale. Nothing touches the ledger while the
        // tab is open. The optional card hold is a manual-capture card-present PaymentIntent.
        //
        // Deliberate deviation from "status pending while the reader collects": a tab being closed
        // by card stays 'open' until its PaymentIntent succeeds. The pending-sale reconciler sweeps
        // 'pending' rows by created_at age, and a tab is hours old by the time it closes, so a
        // 'pending' tab would be abandoned on the reconciler's next tick. The finalizer flips
        // 'open' -> 'paid' on success exactly as it does 'pending'.

        // Lets the existing ResolveCartLines/ApplyDiscounts machinery run over a tab request.
        private static ConcessionSaleRequest AsSaleRequest(
            List<ConcessionSaleRequest.SaleLine> items, ConcessionDiscountInput? discount, string? managerPin, string? customerName) => new()
        {
            Items = items,
            Discount = discount,
            ManagerPin = managerPin,
            CustomerName = customerName,
        };

        // Folds a round's (or the close's) discount outcome into the sale-level snapshot.
        private static void MergeDiscountOutcome(ConcessionSale sale, DiscountOutcome disc)
        {
            var kind = ConcessionTabRules.MergeDiscountKind(sale.DiscountKind, disc.DiscountKind);
            sale.DiscountKind = kind;
            if (kind == "mixed") sale.DiscountLabel = "Discounts";
            else if (!string.IsNullOrEmpty(disc.DiscountLabel)) sale.DiscountLabel = disc.DiscountLabel;
            if (disc.CompReasonId is not null) { sale.CompReasonId = disc.CompReasonId; sale.CompReasonLabel = disc.CompReasonLabel; }
            if (disc.AuthorizedByUserId is not null) { sale.AuthorizedByUserId = disc.AuthorizedByUserId; sale.AuthorizedByName = disc.AuthorizedByName; }
            if (disc.PurchaserUserId is not null)
            {
                sale.PurchaserUserId = disc.PurchaserUserId;
                sale.PurchaserEmail = disc.PurchaserEmail;
                sale.PurchaserName = disc.PurchaserName;
            }
        }

        private static string TabNotOpenMessage(ConcessionSale sale) => sale.Status switch
        {
            "paid" => "This tab was already closed.",
            "voided" => "This tab was voided.",
            _ => "This tab is no longer open.",
        };

        [Authorize(Policy = TenantPermissions.Policy.ConcessionsCounter)]
        [HttpPost("Tab")]
        public async Task<IActionResult> OpenTab([FromBody] ConcessionOpenTabRequest req, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!_tenantContext.Tenant.ConcessionsEnabled)
                return new ApiResponses().BadRequestResult("Food & Beverage isn't enabled for this track.");
            var tenantId = _tenantContext.TenantId;
            var menuSettings = await _concessions.GetMenuSettings(tenantId);
            if (!(menuSettings?.TabsEnabled ?? true))
                return new ApiResponses().BadRequestResult("Open tabs aren't enabled for this venue.");
            var pricesIncludeTax = menuSettings?.PricesIncludeTax ?? false;

            var name = Blank(req.Name);
            if (name is null && !req.HoldCard)
                return new ApiResponses().BadRequestResult("Give the tab a name, or hold a card to name it from the card.");

            Guid? soldBy = Guid.TryParse(User.FindFirst("UserId")?.Value, out var uid) ? uid : null;
            var saleReq = AsSaleRequest(req.Items, req.Discount, req.ManagerPin, req.CustomerName);
            var requestedItems = req.Items.Where(i => i.Quantity > 0).ToList();

            // First round (may be empty: open the tab, ring later). Discounts apply to these lines; a
            // member perk with no lines still attaches the customer to the tab.
            var lines = new List<ConcessionSaleLine>();
            var subtotal = 0;
            if (requestedItems.Count > 0)
            {
                string? cartError;
                (lines, subtotal, cartError) = await ResolveCartLines(tenantId, req.Items);
                if (cartError is not null) return new ApiResponses().BadRequestResult(cartError);
            }
            var (discOk, discErr, disc) = await ApplyDiscounts(tenantId, soldBy ?? Guid.Empty, lines, requestedItems, saleReq, menuSettings, pricesIncludeTax);
            if (!discOk) return new ApiResponses().BadRequestResult(discErr!);

            // Card hold prerequisites are checked before anything is written.
            string? connectedAccountId = null;
            string? locationId = null;
            if (req.HoldCard)
            {
                if (_tenantContext.Tenant.StripeChargeMode == "direct" && string.IsNullOrEmpty(_tenantContext.Tenant.StripeConnectAccountId))
                    return new ApiResponses().BadRequestResult(
                        "This track is set to charge on its own Stripe account but hasn't connected one yet.");
                connectedAccountId = DirectConnectedAccountId();
                locationId = await EnsureTerminalLocation(connectedAccountId, ct);
                if (locationId is null)
                    return new ApiResponses().BadRequestResult(
                        "Cannot hold a card until the track's address is filled in (Settings -> General).");
            }

            var taxCents = lines.Sum(l => l.TaxCents);
            var total = subtotal - disc.DiscountCents + (pricesIncludeTax ? 0 : taxCents);
            // Order number at OPEN so every kitchen ticket for the tab carries one.
            var orderNumber = await _concessions.NextOrderNumber(tenantId);
            var sale = new ConcessionSale
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Status = "open",
                FulfillmentStatus = "active",
                OrderNumber = orderNumber,
                SubtotalCents = subtotal,
                TipCents = 0,
                TaxCents = taxCents,
                PricesIncludeTax = pricesIncludeTax,
                DiscountCents = disc.DiscountCents,
                DiscountKind = disc.DiscountKind,
                DiscountLabel = disc.DiscountLabel,
                CompReasonId = disc.CompReasonId,
                CompReasonLabel = disc.CompReasonLabel,
                AuthorizedByUserId = disc.AuthorizedByUserId,
                AuthorizedByName = disc.AuthorizedByName,
                TotalCents = Math.Max(0, total),
                // payment_method is NOT NULL (stripe|stripe_direct|cash); 'cash' is the placeholder
                // while open and is overwritten by whatever tender actually closes the tab.
                PaymentMethod = "cash",
                StripeConnectedAccountId = connectedAccountId,
                SoldByUserId = soldBy,
                PurchaserUserId = disc.PurchaserUserId,
                PurchaserEmail = disc.PurchaserEmail,
                PurchaserName = disc.PurchaserName ?? Blank(req.CustomerName),
                TabName = name,
                TabOpenedAt = DateTime.UtcNow,
            };
            sale.Id = await _concessions.CreateSale(sale);
            if (lines.Count > 0)
            {
                foreach (var l in lines) l.RoundNumber = 1;
                await _concessions.CreateSaleLines(sale.Id, lines);
                await _concessions.RecomputeSaleFulfillment(sale.Id, tenantId);
                // Round 1 is cooking now, so its ingredients leave stock now.
                try { await _concessions.DepleteInventoryForSale(sale.Id, tenantId, 1); } catch { /* inventory is best-effort */ }
                await NotifyLowStock(tenantId);
            }

            PaymentIntentCreated? hold = null;
            var holdCents = Math.Max(ConcessionTabRules.CardMinimumCents, menuSettings?.TabHoldCents ?? 5000);
            if (req.HoldCard)
            {
                var metadata = new Dictionary<string, string>
                {
                    ["tenant_id"] = tenantId.ToString(),
                    ["sale_kind"] = "concession_tab_hold",
                    ["concession_sale_id"] = sale.Id.ToString(),
                };
                try
                {
                    hold = await _payments.CreateCardPresentHoldPaymentIntentAsync(holdCents, "usd", locationId!, metadata,
                        connectedAccountId: connectedAccountId, ct: ct);
                    if (!await _concessions.SetTabHold(sale.Id, tenantId, hold.IntentId, holdCents))
                    {
                        // The tab stopped being open between its insert and this write (voided from
                        // another register); never leave an authorization nothing owns.
                        await CancelIntent(hold.IntentId, connectedAccountId, sale.Id, ct);
                        hold = null;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // The tab (and its fired round) already exist; keep it open without a hold rather
                    // than fail the request and invite a duplicate tab. The POS sees no secret.
                    hold = null;
                    _logger.LogWarning(ex, "Card hold could not be started for tab {SaleId}; opened without a hold.", sale.Id);
                }
            }
            if (name is null && hold is null)
            {
                // Nothing to name it from after all; give it a visible placeholder.
                name = $"Tab #{orderNumber}";
                await _concessions.RenameTab(sale.Id, tenantId, name, null);
            }

            return new ApiResponses().OkResult(new ConcessionTabResponse
            {
                SaleId = sale.Id,
                OrderNumber = orderNumber,
                TabName = name,
                TotalCents = sale.TotalCents,
                Rounds = lines.Count > 0 ? 1 : 0,
                HoldClientSecret = hold?.ClientSecret,
                HoldPaymentIntentId = hold?.IntentId,
            });
        }

        // ── Concurrency contract for everything below ─────────────────────────────────────────
        // Two registers can work the same tab, and Stripe's webhook finalizes card closes on its own
        // schedule, so every MUTATION of one tab (hold confirmed, add a round, close, void) runs under
        // a per-sale Postgres advisory lock (TabLock) and re-reads the sale only once it holds the lock.
        // The lock spans the Stripe round-trips a close needs, which a single DB transaction could not
        // (the shared finalizer writes the same row on its own connection and would self-deadlock).
        // Underneath, every repository write is a CONDITIONAL update pinned to the exact state read
        // under the lock (status 'open', which PaymentIntent the row points at); 0 rows ⇒ 409 and the
        // attempt is rolled back (its store-credit redeem reversed). A tab with a card close in flight
        // (status 'open' + stripe_payment_intent_id set) refuses rounds and other tenders until that
        // intent is settled at Stripe FIRST (SettleInFlightCard): cancelled, or found to have succeeded,
        // in which case it is finalized and the cashier is told it already went through.
        private const string TabChangedMessage = "This tab is being closed on another register.";
        private const string CardInFlightMessage =
            "A card payment is in progress for this tab. Finish or cancel it on the register closing the tab before adding a round.";
        private const string CardAlreadyPaidMessage = "The earlier card payment for this tab already went through; the tab is paid.";
        private const string HoldAlreadyChargedMessage = "The card on file was already charged for this tab, so it is paid by card.";

        private Task<IAsyncDisposable> TabLock(Guid saleId) => _db.AcquireAdvisoryLock($"concession-tab:{saleId}");

        // Best-effort cancel. The provider already re-reads a rejected cancel's real status (most
        // importantly 'succeeded'); null means Stripe could not be reached at all.
        private async Task<string?> CancelIntent(string intentId, string? connectedAccountId, Guid saleId, CancellationToken ct)
        {
            try { return await _payments.CancelPaymentIntentAsync(intentId, connectedAccountId, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Could not cancel PaymentIntent {IntentId} for tab {SaleId}.", intentId, saleId);
                return null;
            }
        }

        // An earlier close attempt's PaymentIntent (a card on the reader, or the hold being captured)
        // is still pointed at this open tab. Before anything else changes it is settled at Stripe:
        // cancelled, so it can never charge (the attempt is over and the caller proceeds), or found to
        // have succeeded (the tab IS paid by it: finalize and say so). The one intent that is kept is a
        // hold still awaiting capture when the cashier retries the 'hold' tender; the capture path
        // re-arms it. Returns null to proceed, otherwise the response to send.
        private async Task<IActionResult?> SettleInFlightCard(ConcessionSale sale, string tender, CancellationToken ct)
        {
            var pi = sale.StripePaymentIntentId!;
            if (tender == "hold" && pi == sale.HoldPaymentIntentId)
            {
                var status = await _payments.GetPaymentIntentStatusAsync(pi, sale.StripeConnectedAccountId, ct);
                if (status == "requires_capture") return null;
                if (status == "succeeded") return await FinalizeAlreadyPaid(sale, pi, CardAlreadyPaidMessage, ct);
                // Expired, cancelled or unknown: cancelled and released below like any other stale intent.
            }
            var result = await CancelIntent(pi, sale.StripeConnectedAccountId, sale.Id, ct);
            switch (ConcessionTabRules.ResolveInFlightCancel(result))
            {
                case InFlightCardOutcome.AlreadyPaid:
                    return await FinalizeAlreadyPaid(sale, pi, CardAlreadyPaidMessage, ct);
                case InFlightCardOutcome.Blocked:
                    return new ApiResponses().BadRequestResult(result is null
                        ? "Could not reach the card processor to cancel the earlier card payment for this tab. Try again."
                        : "The earlier card payment for this tab is still processing on the reader. Wait a moment and try again.");
            }
            if (!await _concessions.ClearTabCardPayment(sale.Id, sale.TenantId, pi))
                return new ApiResponses().ConflictResult(TabChangedMessage);
            sale.StripePaymentIntentId = null;
            sale.PaymentMethod = "cash";
            // When that intent WAS the hold (a capture attempt with no known outcome), cancelling it
            // released the hold with it.
            if (pi == sale.HoldPaymentIntentId && await _concessions.SetTabHold(sale.Id, sale.TenantId, null, null))
            {
                sale.HoldPaymentIntentId = null;
                sale.HoldAmountCents = null;
            }
            return null;
        }

        // The intent already succeeded at Stripe: the tab is paid by it, whatever this request asked.
        // The row already carries that attempt's totals and credit, so the finalizer books them as is.
        private async Task<IActionResult> FinalizeAlreadyPaid(ConcessionSale sale, string paymentIntentId, string message, CancellationToken ct)
        {
            await _finalizer.ProcessPaymentIntentAsync(paymentIntentId, "payment_intent.succeeded", ct);
            return new ApiResponses().BadRequestResult(message);
        }

        private enum HoldRelease { Released, Captured, Blocked }

        // Releases the card hold a tab no longer needs (cash/credit close, or a different card on the
        // reader). Released: the tab may proceed without it (cancelled, or already dead). Captured: the
        // hold turned out charged, so that capture is the tab's payment and the caller books it.
        // Blocked: Stripe could not confirm either way; the caller decides whether that matters (an
        // uncaptured hold charges nothing and lapses on its own, unless a capture of it is in flight).
        // A hold the row was pointing at as its settling intent (a re-armed capture attempt) is taken
        // off the row with it, so the cash flip that follows sees no intent in flight.
        private async Task<HoldRelease> TryReleaseHold(ConcessionSale sale, CancellationToken ct)
        {
            var holdPi = sale.HoldPaymentIntentId;
            if (string.IsNullOrEmpty(holdPi)) return HoldRelease.Released;
            var result = await CancelIntent(holdPi, sale.StripeConnectedAccountId, sale.Id, ct);
            switch (ConcessionTabRules.ResolveInFlightCancel(result))
            {
                case InFlightCardOutcome.AlreadyPaid:
                    return HoldRelease.Captured;
                case InFlightCardOutcome.Released:
                    if (sale.StripePaymentIntentId == holdPi && await _concessions.ClearTabCardPayment(sale.Id, sale.TenantId, holdPi))
                    {
                        sale.StripePaymentIntentId = null;
                        sale.PaymentMethod = "cash";
                    }
                    if (await _concessions.SetTabHold(sale.Id, sale.TenantId, null, null))
                    {
                        sale.HoldPaymentIntentId = null;
                        sale.HoldAmountCents = null;
                    }
                    return HoldRelease.Released;
                default:
                    _logger.LogWarning("Card hold {IntentId} on tab {SaleId} could not be released ({Result}).",
                        holdPi, sale.Id, result ?? "unreachable");
                    return HoldRelease.Blocked;
            }
        }

        // Undoes what a failed close attempt did to the tab: the store credit it redeemed goes back
        // (exactly once, by its own attempt reference) and the row stops claiming it. The money
        // figures are left; the next attempt rebuilds them from the lines anyway.
        private async Task RollBackAttempt(ConcessionSale sale, string note)
        {
            if (await ConcessionTabCredit.ReleaseAsync(_credit, sale, note))
                await _concessions.UpdateTabTotals(sale);
        }

        // After the reader authorized the hold: read the card's brand/last4/cardholder and label the
        // tab with them (auto-naming it when the cashier left the name blank).
        [Authorize(Policy = TenantPermissions.Policy.ConcessionsCounter)]
        [HttpPost("Tab/{id:guid}/HoldConfirmed")]
        public async Task<IActionResult> TabHoldConfirmed(Guid id, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var tenantId = _tenantContext.TenantId;
            await using var tabLock = await TabLock(id);
            var sale = await _concessions.GetSale(id, tenantId);
            if (sale is null || !sale.IsTab) return new ApiResponses().NotFoundResult("Tab not found.");
            if (sale.Status != "open") return new ApiResponses().BadRequestResult(TabNotOpenMessage(sale));
            if (string.IsNullOrEmpty(sale.HoldPaymentIntentId))
                return new ApiResponses().BadRequestResult("No card hold was started for this tab.");

            var details = await _payments.GetCardPresentDetailsAsync(sale.HoldPaymentIntentId, sale.StripeConnectedAccountId, ct);
            if (details is null || details.Status is not ("requires_capture" or "succeeded"))
                return new ApiResponses().BadRequestResult("The card hold hasn't been confirmed on the reader yet.");

            if (!await _concessions.SetTabHoldCard(sale.Id, tenantId, sale.HoldPaymentIntentId, details.Brand, details.Last4, details.CardholderName,
                    ConcessionTabRules.AutoTabName(details.Brand, details.Last4, details.CardholderName) ?? $"Tab #{sale.OrderNumber}"))
                return new ApiResponses().ConflictResult(TabChangedMessage);
            var fresh = await _concessions.GetSale(id, tenantId);
            return new ApiResponses().OkResult(await BuildTabDetail(fresh ?? sale));
        }

        // Another round: fires to the kitchen now, depletes its own inventory now, payment later.
        [Authorize(Policy = TenantPermissions.Policy.ConcessionsCounter)]
        [HttpPost("Tab/{id:guid}/Round")]
        public async Task<IActionResult> AddTabRound(Guid id, [FromBody] ConcessionTabRoundRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var tenantId = _tenantContext.TenantId;
            var requestedItems = req.Items.Where(i => i.Quantity > 0).ToList();
            if (requestedItems.Count == 0) return new ApiResponses().BadRequestResult("Add at least one item to the round.");

            await using var tabLock = await TabLock(id);
            var sale = await _concessions.GetSale(id, tenantId);
            if (sale is null || !sale.IsTab) return new ApiResponses().NotFoundResult("Tab not found.");
            if (sale.Status != "open") return new ApiResponses().BadRequestResult(TabNotOpenMessage(sale));
            // A card close in flight has its tip/discount/credit persisted and a PaymentIntent for that
            // exact due; a round now would make the reader charge a stale amount. The register closing
            // the tab settles or cancels it first.
            if (ConcessionTabRules.HasCardInFlight(sale.Status, sale.StripePaymentIntentId))
                return new ApiResponses().ConflictResult(CardInFlightMessage);

            var menuSettings = await _concessions.GetMenuSettings(tenantId);
            var pricesIncludeTax = menuSettings?.PricesIncludeTax ?? false;
            Guid? soldBy = Guid.TryParse(User.FindFirst("UserId")?.Value, out var uid) ? uid : null;

            var (lines, _, cartError) = await ResolveCartLines(tenantId, req.Items);
            if (cartError is not null) return new ApiResponses().BadRequestResult(cartError);
            var saleReq = AsSaleRequest(req.Items, req.Discount, req.ManagerPin, null);
            var (discOk, discErr, disc) = await ApplyDiscounts(tenantId, soldBy ?? Guid.Empty, lines, requestedItems, saleReq, menuSettings, pricesIncludeTax);
            if (!discOk) return new ApiResponses().BadRequestResult(discErr!);

            // Sale-level money is rebuilt from EVERY round's lines (tip stays 0 until close): the rounds
            // already stored, read once, plus the ones about to be. Combo children carry no money of
            // their own, so the stored child rows and the in-memory parents sum the same way.
            var existing = await _concessions.GetSaleLines(sale.Id);
            var totals = ConcessionTabRules.Recompute(existing.Concat(lines), pricesIncludeTax, tipCents: 0);
            sale.SubtotalCents = totals.SubtotalCents;
            sale.DiscountCents = totals.DiscountCents;
            sale.TaxCents = totals.TaxCents;
            sale.TipCents = 0;
            sale.TotalCents = totals.TotalCents;
            MergeDiscountOutcome(sale, disc);
            // Round number, line inserts and the new totals land together or not at all.
            var round = await _concessions.AppendTabRound(sale, lines);
            if (round is null) return new ApiResponses().ConflictResult(TabChangedMessage);
            // New lines are queued, so the tab is back to 'active' for the kitchen.
            await _concessions.RecomputeSaleFulfillment(sale.Id, tenantId);
            try { await _concessions.DepleteInventoryForSale(sale.Id, tenantId, round.Value); } catch { /* inventory is best-effort */ }
            await NotifyLowStock(tenantId);

            var fresh = await _concessions.GetSale(id, tenantId);
            // The detail is answered from the lines already in hand (stored rows + the ones just
            // inserted, each combo parent followed by its children) rather than a third read.
            var all = existing.Concat(lines.SelectMany(l => l.Children.Prepend(l))).ToList();
            return new ApiResponses().OkResult(BuildTabDetail(fresh ?? sale, all));
        }

        [Authorize(Policy = TenantPermissions.Policy.ConcessionsCounter)]
        [HttpGet("Tabs")]
        public async Task<IActionResult> ListOpenTabs()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var tabs = await _concessions.ListOpenTabs(_tenantContext.TenantId);
            return new ApiResponses().OkResult(tabs.Select(ToTabSummary).ToList());
        }

        [Authorize(Policy = TenantPermissions.Policy.ConcessionsCounter)]
        [HttpGet("Tab/{id:guid}")]
        public async Task<IActionResult> GetTab(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var sale = await _concessions.GetSale(id, _tenantContext.TenantId);
            if (sale is null || !sale.IsTab) return new ApiResponses().NotFoundResult("Tab not found.");
            return new ApiResponses().OkResult(await BuildTabDetail(sale));
        }

        [Authorize(Policy = TenantPermissions.Policy.ConcessionsCounter)]
        [HttpPut("Tab/{id:guid}")]
        public async Task<IActionResult> RenameTab(Guid id, [FromBody] ConcessionRenameTabRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var name = Blank(req.Name);
            if (name is null) return new ApiResponses().BadRequestResult("Tab name is required.");
            var sale = await _concessions.GetSale(id, _tenantContext.TenantId);
            if (sale is null || !sale.IsTab) return new ApiResponses().NotFoundResult("Tab not found.");
            if (sale.Status != "open") return new ApiResponses().BadRequestResult(TabNotOpenMessage(sale));
            if (!await _concessions.RenameTab(sale.Id, _tenantContext.TenantId, name, Blank(req.CustomerName)))
                return new ApiResponses().BadRequestResult("This tab is no longer open.");
            var fresh = await _concessions.GetSale(id, _tenantContext.TenantId);
            return new ApiResponses().OkResult(await BuildTabDetail(fresh ?? sale));
        }

        // Settle the tab. Totals are rebuilt from every round (+ optional close-time order discount +
        // tip), store credit applied like CreateSale but under a per-ATTEMPT reference (see
        // ConcessionTabCredit), then: cash / nothing due -> paid now; card -> a new card-present PI for
        // the reader (finalized by Sale/{id}/Finalize or the webhook); hold -> capture the held
        // authorization (topped up by incremental auth when the network allows), finalized through
        // the same path. A close can be ATTEMPTED repeatedly (declined card, tender switch); each
        // attempt first settles whatever intent the previous one left in flight, hands back the credit
        // it redeemed, and re-plans from the untouched lines.
        [Authorize(Policy = TenantPermissions.Policy.ConcessionsCounter)]
        [HttpPost("Tab/{id:guid}/Close")]
        public async Task<IActionResult> CloseTab(Guid id, [FromBody] ConcessionCloseTabRequest req, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var tenantId = _tenantContext.TenantId;
            await using var tabLock = await TabLock(id);
            var sale = await _concessions.GetSale(id, tenantId);
            if (sale is null || !sale.IsTab) return new ApiResponses().NotFoundResult("Tab not found.");
            if (sale.Status != "open") return new ApiResponses().BadRequestResult(TabNotOpenMessage(sale));

            var menuSettings = await _concessions.GetMenuSettings(tenantId);
            var pricesIncludeTax = menuSettings?.PricesIncludeTax ?? false;
            var tipCents = (menuSettings?.TipsEnabled ?? false) ? Math.Max(0, req.TipCents) : 0;
            Guid? soldBy = Guid.TryParse(User.FindFirst("UserId")?.Value, out var uid) ? uid : null;
            var tender = (req.PaymentMethod ?? "").Trim().ToLowerInvariant();

            var lines = await _concessions.GetSaleLines(sale.Id);
            if (lines.Count == 0 || lines.Sum(l => l.LineTotalCents + l.DiscountCents) <= 0)
                return new ApiResponses().BadRequestResult("This tab has nothing on it. Void it instead of closing.");

            // ── 1. An earlier attempt's card intent still in flight is settled at Stripe first. ───
            if (!string.IsNullOrEmpty(sale.StripePaymentIntentId)
                && await SettleInFlightCard(sale, tender, ct) is { } settled)
                return settled;

            // ── 2. Credit an earlier attempt redeemed goes back before this attempt re-plans it. ──
            // Reversal is by that attempt's own reference, so it happens exactly once however many
            // times the close is retried; the row stops claiming the credit right away.
            if (await ConcessionTabCredit.ReleaseAsync(_credit, sale, "tab close retried")
                && !await _concessions.UpdateTabTotals(sale))
                return new ApiResponses().ConflictResult(TabChangedMessage);

            // ── 3. Money, rebuilt from the lines. ──────────────────────────────────────────────────
            // The sale-level discount kind/label start from what the rounds actually carry (each round's
            // discounts live on its lines), so a close attempt never inherits a label from an earlier,
            // failed attempt's close-time discount.
            var lineKinds = lines.Where(l => l.DiscountCents > 0 && !string.IsNullOrEmpty(l.DiscountKind))
                                 .Select(l => l.DiscountKind!).Distinct().ToList();
            sale.DiscountKind = lineKinds.Count switch { 0 => null, 1 => lineKinds[0], _ => "mixed" };
            sale.DiscountLabel = lineKinds.Count switch
            {
                0 => null,
                1 => lines.First(l => l.DiscountCents > 0 && l.DiscountKind == lineKinds[0]).DiscountLabel,
                _ => "Discounts",
            };

            // Close-time order-level discount: the same engine as a counter sale, spread across every
            // round's lines IN MEMORY (percent / dollar / preset / comp / member perk, PIN-gated by kind
            // as usual). Only the sale-level figures it produces are persisted; the stored lines keep
            // their round-level values. That is what makes a retry safe: it rebuilds the discount from
            // the untouched lines instead of stacking it, and a retry without the discount drops it.
            if (req.Discount is not null && !string.IsNullOrEmpty(req.Discount.Kind))
            {
                var saleReq = AsSaleRequest(new(), req.Discount, req.ManagerPin, null);
                var (discOk, discErr, disc) = await ApplyDiscounts(tenantId, soldBy ?? Guid.Empty, lines, new(), saleReq, menuSettings, pricesIncludeTax);
                if (!discOk) return new ApiResponses().BadRequestResult(discErr!);
                MergeDiscountOutcome(sale, disc);
            }

            var totals = ConcessionTabRules.Recompute(lines, pricesIncludeTax, tipCents);
            sale.SubtotalCents = totals.SubtotalCents;
            sale.DiscountCents = totals.DiscountCents;
            sale.TaxCents = totals.TaxCents;
            sale.TipCents = tipCents;
            sale.TotalCents = totals.TotalCents;

            // Store credit tender, capped at balance + total like CreateSale. The balance read here is
            // the real one: step 2 already handed back anything this tab had reserved.
            Services.Repositories.Data.CreditData.TenantCreditAccount? creditAccount = null;
            var creditApplied = 0;
            if (req.CreditAccountId is not null && req.CreditCents > 0 && sale.TotalCents > 0)
            {
                creditAccount = await _credit.GetAccount(req.CreditAccountId.Value, tenantId);
                if (creditAccount is null)
                    return new ApiResponses().BadRequestResult("That store credit account no longer exists. Look it up again.");
                creditApplied = Math.Min(Math.Min(req.CreditCents, creditAccount.BalanceCents), sale.TotalCents);
            }
            var due = sale.TotalCents - creditApplied;

            // ── 4. Which path, and everything that can be checked before anything is written. ─────
            var path = ConcessionTabRules.ResolveClosePath(tender, due, sale.HoldPaymentIntentId, sale.HoldAmountCents);
            switch (path)
            {
                case TabClosePath.Invalid:
                    return new ApiResponses().BadRequestResult("Choose how the tab is being paid: cash, card, or the card on file.");
                case TabClosePath.NoHold:
                    return new ApiResponses().BadRequestResult("No card is held on this tab. Collect a card or take cash.");
                case TabClosePath.CardBelowMinimum:
                    // Stripe rejects a capture below the minimum exactly like a charge below it, so a
                    // sub-50¢ remainder on a held card is a cash remainder too.
                    return new ApiResponses().BadRequestResult(creditApplied > 0
                        ? "Less than 50 cents is due after credit. Take cash for the remainder."
                        : "Card sale total must be at least 50 cents.");
                case TabClosePath.HoldTooSmall:
                    // Top the authorization up where the card network allows; otherwise the POS falls
                    // back to collecting the card again.
                    if (await _payments.IncrementAuthorizationAsync(sale.HoldPaymentIntentId!, due, sale.StripeConnectedAccountId, ct)
                        && await _concessions.SetTabHold(sale.Id, tenantId, sale.HoldPaymentIntentId, due))
                    {
                        sale.HoldAmountCents = due;
                        path = TabClosePath.CaptureHold;
                        break;
                    }
                    return new ApiResponses().BadRequestResult("The tab total exceeds the card hold. Collect the card again.");
            }
            string? connectedAccountId = null;
            string? locationId = null;
            if (path == TabClosePath.CardPresent)
            {
                if (_tenantContext.Tenant.StripeChargeMode == "direct" && string.IsNullOrEmpty(_tenantContext.Tenant.StripeConnectAccountId))
                    return new ApiResponses().BadRequestResult(
                        "This track is set to charge on its own Stripe account but hasn't connected one yet.");
                connectedAccountId = DirectConnectedAccountId();
                locationId = await EnsureTerminalLocation(connectedAccountId, ct);
                if (locationId is null)
                    return new ApiResponses().BadRequestResult(
                        "Cannot take card-present payments until the track's address is filled in (Settings -> General).");
            }

            // ── 5. This attempt's redeem (its own reference) and the totals, pinned to the state read
            //       under the lock. From here on every early exit rolls the attempt back. ─────────────
            if (creditApplied > 0 && !await ConcessionTabCredit.RedeemAsync(_credit, sale, creditAccount!.Id, creditApplied, soldBy))
                return new ApiResponses().BadRequestResult("The store credit balance changed while closing. Look the customer up again.");
            if (!await _concessions.UpdateTabTotals(sale))
            {
                await RollBackAttempt(sale, "tab changed on another register");
                return new ApiResponses().ConflictResult(TabChangedMessage);
            }

            var holdForAudit = sale.HoldPaymentIntentId;
            async Task AuditClose(string method, int collectedCents) => await _audit.Log(
                "concession.tab_close",
                $"Closed tab \"{sale.TabName}\" for ${sale.TotalCents / 100m:0.00} ({method})",
                targetKind: "concession_sale", targetId: sale.Id, tenantId: tenantId,
                metadata: new
                {
                    tabName = sale.TabName,
                    totalCents = sale.TotalCents,
                    tipCents = sale.TipCents,
                    discountCents = sale.DiscountCents,
                    creditAppliedCents = creditApplied,
                    dueCents = Math.Max(0, due),
                    collectedCents,
                    paymentMethod = method,
                    rounds = lines.Select(l => l.RoundNumber).Distinct().Count(),
                    holdPaymentIntentId = holdForAudit,
                });

            // The hold is released BEFORE any other tender settles the tab, so a captured hold can never
            // sit behind a tab recorded as paid by cash or by another card. Captured out of band: that
            // capture IS the payment (point the row at it, book it, tell the cashier). Blocked: fine to
            // go on when the hold is merely parked (it lapses uncharged), not when a capture of it is
            // what this tab was waiting on, because its outcome is then unknown.
            async Task<IActionResult?> ReleaseHoldOrBookIt()
            {
                var holdPi = sale.HoldPaymentIntentId;
                switch (await TryReleaseHold(sale, ct))
                {
                    case HoldRelease.Captured:
                        var method = string.IsNullOrEmpty(sale.StripeConnectedAccountId) ? "stripe" : "stripe_direct";
                        if (!await _concessions.SetTabCardPayment(sale.Id, tenantId, holdPi!, method, sale.StripeConnectedAccountId))
                            return new ApiResponses().ConflictResult(TabChangedMessage);
                        sale.StripePaymentIntentId = holdPi;
                        _logger.LogWarning("Card hold {IntentId} on tab {SaleId} was already captured when the tab closed by {Tender}; booking it as the payment.", holdPi, sale.Id, tender);
                        await AuditClose(method + " (hold, captured out of band)", due);
                        return await FinalizeAlreadyPaid(sale, holdPi!, HoldAlreadyChargedMessage, ct);
                    case HoldRelease.Blocked when sale.StripePaymentIntentId == holdPi:
                        await RollBackAttempt(sale, "hold release unconfirmed");
                        return new ApiResponses().BadRequestResult("Could not reach the card processor to release the card hold. Try again or use the card on file.");
                    default:
                        return null;
                }
            }

            // ── Paid at the counter: cash, or credit/comp covered it all. ─────────────────────────
            if (path == TabClosePath.PaidNow)
            {
                if (await ReleaseHoldOrBookIt() is { } holdOutcome) return holdOutcome;
                if (!await _concessions.CloseTabPaid(sale.Id, tenantId, "cash"))
                {
                    await RollBackAttempt(sale, "tab changed on another register");
                    return new ApiResponses().ConflictResult(TabChangedMessage);
                }
                sale.Status = "paid";
                sale.PaymentMethod = "cash";
                var collected = sale.TotalCents - creditApplied;
                if (collected > 0) await WriteCashLedger(sale);   // skip when no money changed hands
                await _concessions.RecomputeSaleFulfillment(sale.Id, tenantId);
                await _concessions.CompleteTabIfAllServed(sale.Id, tenantId);
                await AuditClose("cash", collected);
                return new ApiResponses().OkResult(new ConcessionSaleResponse
                {
                    SaleId = sale.Id,
                    TotalCents = sale.TotalCents,
                    DiscountCents = sale.DiscountCents,
                    CreditAppliedCents = creditApplied,
                    DueCents = Math.Max(0, due),
                    Status = "paid",
                    OrderNumber = sale.OrderNumber,
                });
            }

            // ── Capture the hold placed when the tab was opened. ──────────────────────────────────
            if (path == TabClosePath.CaptureHold)
            {
                var holdPi = sale.HoldPaymentIntentId!;
                var method = string.IsNullOrEmpty(sale.StripeConnectedAccountId) ? "stripe" : "stripe_direct";
                // Point the sale at the hold PI BEFORE capturing (the succeeded webhook can beat our own
                // finalize call), and capture only once that write is known to have matched the row.
                if (!await _concessions.SetTabCardPayment(sale.Id, tenantId, holdPi, method, sale.StripeConnectedAccountId))
                {
                    await RollBackAttempt(sale, "tab changed on another register");
                    return new ApiResponses().ConflictResult(TabChangedMessage);
                }
                sale.StripePaymentIntentId = holdPi;
                sale.PaymentMethod = method;
                var captured = await _payments.CapturePaymentIntentAsync(holdPi, due, sale.StripeConnectedAccountId, ct);
                if (captured == "succeeded")
                {
                    await _finalizer.ProcessPaymentIntentAsync(holdPi, "payment_intent.succeeded", ct);
                    var paidSale = await _concessions.GetSale(id, tenantId);
                    await AuditClose(method + " (hold)", due);
                    return new ApiResponses().OkResult(new ConcessionSaleResponse
                    {
                        SaleId = sale.Id,
                        PaymentIntentId = holdPi,
                        TotalCents = sale.TotalCents,
                        DiscountCents = sale.DiscountCents,
                        CreditAppliedCents = creditApplied,
                        DueCents = due,
                        Status = paidSale?.Status ?? "paid",
                        OrderNumber = paidSale?.OrderNumber ?? sale.OrderNumber,
                    });
                }
                if (captured is null)
                {
                    // Outcome unknown (processor unreachable). The row keeps pointing at the hold, so
                    // the succeeded webhook books it if it did land, and the next close attempt settles
                    // it at Stripe before doing anything else. This attempt's credit stays with it.
                    return new ApiResponses().BadRequestResult("Could not reach the card processor to capture the hold. Try again or collect the card.");
                }
                // Definitive: the hold can't be captured (expired, cancelled). Undo this attempt; a hold
                // Stripe still reports as capturable is left on the tab for another try.
                if (await _concessions.ClearTabCardPayment(sale.Id, tenantId, holdPi))
                {
                    sale.StripePaymentIntentId = null;
                    sale.PaymentMethod = "cash";
                }
                if (captured != "requires_capture" && await _concessions.SetTabHold(sale.Id, tenantId, null, null))
                {
                    sale.HoldPaymentIntentId = null;
                    sale.HoldAmountCents = null;
                }
                await RollBackAttempt(sale, "hold capture failed");
                return new ApiResponses().BadRequestResult("The card hold could not be captured. Collect the card again.");
            }

            // ── A (different) card on the reader: new card-present PI, same finalize path as a sale. ─
            // The hold is no longer needed once a different card is collected; it is released before
            // the new intent exists, so a hold that turns out captured never competes with a second charge.
            if (await ReleaseHoldOrBookIt() is { } holdOutcome2) return holdOutcome2;
            var cardMetadata = new Dictionary<string, string>
            {
                ["tenant_id"] = tenantId.ToString(),
                ["sale_kind"] = "concession",
                ["concession_sale_id"] = sale.Id.ToString(),
            };
            PaymentIntentCreated intent;
            try
            {
                intent = await _payments.CreateCardPresentPaymentIntentAsync(due, "usd", locationId!, cardMetadata, null,
                    connectedAccountId: connectedAccountId, ct: ct);
            }
            catch (InvalidOperationException ex)
            {
                await RollBackAttempt(sale, "payment could not start");
                return new ApiResponses().BadRequestResult(ex.Message);
            }
            var cardMethod = string.IsNullOrEmpty(connectedAccountId) ? "stripe" : "stripe_direct";
            if (!await _concessions.SetTabCardPayment(sale.Id, tenantId, intent.IntentId, cardMethod, connectedAccountId))
            {
                // Never leave a chargeable intent behind a tab that moved on.
                await CancelIntent(intent.IntentId, connectedAccountId, sale.Id, ct);
                await RollBackAttempt(sale, "tab changed on another register");
                return new ApiResponses().ConflictResult(TabChangedMessage);
            }
            // Status stays 'open' in the database until the PI succeeds (see the section comment);
            // 'pending' here tells the POS to run the reader flow, as it does for a counter sale.
            return new ApiResponses().OkResult(new ConcessionSaleResponse
            {
                SaleId = sale.Id,
                ClientSecret = intent.ClientSecret,
                PaymentIntentId = intent.IntentId,
                TotalCents = sale.TotalCents,
                DiscountCents = sale.DiscountCents,
                CreditAppliedCents = creditApplied,
                DueCents = due,
                Status = "pending",
                OrderNumber = sale.OrderNumber,
            });
        }

        // Tear up a tab. Manager PIN like a refund; fired items are NOT restocked (the kitchen made
        // them); every outstanding line leaves the KDS. Any in-flight card intent and the hold are
        // cancelled at Stripe BEFORE the flip, and the void is refused (tab stays open) when a cancel
        // fails or the intent already succeeded, in which case the tab is paid and gets a refund instead.
        [Authorize(Policy = TenantPermissions.Policy.ConcessionsCounter)]
        [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("manager-pin")]
        [HttpPost("Tab/{id:guid}/Void")]
        public async Task<IActionResult> VoidTab(Guid id, [FromBody] ConcessionVoidTabRequest req, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!Guid.TryParse(User.FindFirst("UserId")?.Value, out var actingUserId))
                return new ApiResponses().BadRequestResult("Not signed in.");
            var tenantId = _tenantContext.TenantId;

            var pinResult = await _managerPin.VerifyAsync(tenantId, actingUserId, req?.ManagerPin);
            if (!pinResult.Authorized)
                return new ApiResponses().BadRequestResult(pinResult.Error ?? "Manager authorization required to void a tab.");
            var reason = Blank(req?.Reason);
            if (reason is null) return new ApiResponses().BadRequestResult("Give a reason for voiding the tab.");

            await using var tabLock = await TabLock(id);
            var sale = await _concessions.GetSale(id, tenantId);
            if (sale is null || !sale.IsTab) return new ApiResponses().NotFoundResult("Tab not found.");
            if (ConcessionTabRules.VoidBlocker(sale.Status) is { } blocker) return new ApiResponses().BadRequestResult(blocker);
            var method = string.IsNullOrEmpty(sale.StripeConnectedAccountId) ? "stripe" : "stripe_direct";

            // An in-flight card close: cancelled first. 'succeeded' means the customer paid in the
            // window, so the tab is a paid sale now and the manager refunds it rather than voiding it;
            // anything short of a confirmed cancel keeps the tab open rather than leave a live intent.
            var holdPi = sale.HoldPaymentIntentId;
            if (!string.IsNullOrEmpty(sale.StripePaymentIntentId))
            {
                var pi = sale.StripePaymentIntentId;
                switch (ConcessionTabRules.ResolveInFlightCancel(await CancelIntent(pi, sale.StripeConnectedAccountId, sale.Id, ct)))
                {
                    case InFlightCardOutcome.AlreadyPaid:
                        return await FinalizeAlreadyPaid(sale, pi,
                            "The card payment for this tab already went through, so it is paid. Refund it instead of voiding.", ct);
                    case InFlightCardOutcome.Blocked:
                        return new ApiResponses().BadRequestResult(
                            "Could not cancel the card payment in progress for this tab, so it was not voided. Try again in a moment.");
                }
                if (!await _concessions.ClearTabCardPayment(sale.Id, tenantId, pi))
                    return new ApiResponses().ConflictResult(TabChangedMessage);
                sale.StripePaymentIntentId = null;
                if (pi == holdPi) holdPi = null;   // the intent WAS the hold; it is gone with it
            }
            if (!string.IsNullOrEmpty(holdPi))
            {
                switch (ConcessionTabRules.ResolveInFlightCancel(await CancelIntent(holdPi, sale.StripeConnectedAccountId, sale.Id, ct)))
                {
                    case InFlightCardOutcome.AlreadyPaid:
                        // Captured outside this flow: that capture is the tab's payment.
                        if (!await _concessions.SetTabCardPayment(sale.Id, tenantId, holdPi, method, sale.StripeConnectedAccountId))
                            return new ApiResponses().ConflictResult(TabChangedMessage);
                        return await FinalizeAlreadyPaid(sale, holdPi,
                            "The card on file was already charged for this tab, so it is paid. Refund it instead of voiding.", ct);
                    case InFlightCardOutcome.Blocked:
                        return new ApiResponses().BadRequestResult(
                            "Could not release the card hold on this tab, so it was not voided. Try again in a moment.");
                }
            }

            if (!await _concessions.VoidTab(sale.Id, tenantId, reason, pinResult.AuthorizedName))
                return new ApiResponses().ConflictResult(TabChangedMessage);
            // Store credit redeemed by an unfinished close attempt goes back to the customer (by that
            // attempt's own reference, so a repeated void can't hand it back twice).
            await ConcessionTabCredit.ReleaseAsync(_credit, sale, "tab voided");

            await _audit.Log(
                "concession.tab_void",
                $"Voided tab \"{sale.TabName}\" (${sale.TotalCents / 100m:0.00}): {reason}",
                targetKind: "concession_sale",
                targetId: sale.Id,
                tenantId: tenantId,
                metadata: new
                {
                    tabName = sale.TabName,
                    totalCents = sale.TotalCents,
                    discountCents = sale.DiscountCents,
                    reason,
                    holdPaymentIntentId = sale.HoldPaymentIntentId,
                    // Who approved it via manager PIN; the PIN itself is deliberately never recorded.
                    authorizedBy = pinResult.AuthorizedName,
                });
            return new ApiResponses().OkResult();
        }

        // ── Rider online ordering (any logged-in user; pays online via the Payment Element) ─────────
        // Anonymous: the menu is a marketing surface (rendered by the public order page
        // and the embedded F&B widget). Catalog data only, no PII; placing an order
        // still requires an account.
        [HttpGet("Menu")]
        public async Task<IActionResult> RiderMenu()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!_tenantContext.Tenant.ConcessionsEnabled)
                return new ApiResponses().OkResult(new List<ConcessionProductResponse>());
            var products = await _concessions.ListProducts(_tenantContext.TenantId, activeOnly: true);
            return new ApiResponses().OkResult(await Hydrate(products, activeOnly: true));
        }

        // Place + pay for an order online. Server recomputes the total from the catalog, charges the
        // tenant's Stripe account (connected-account aware; concessions are all-in so no application
        // fee), and the webhook finalizes it onto the cook screen with an order number.
        [Authorize]
        [HttpPost("Order")]
        public async Task<IActionResult> RiderOrder([FromBody] ConcessionSaleRequest req, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!_tenantContext.Tenant.ConcessionsEnabled)
                return new ApiResponses().BadRequestResult("Food & Beverage isn't enabled for this track.");
            if (!Guid.TryParse(User.FindFirst("UserId")?.Value, out var userId))
                return new ApiResponses().BadRequestResult("Invalid token.");
            var user = await _users.GetById(userId);
            if (user is null) return new ApiResponses().BadRequestResult("User not found.");

            var tenantId = _tenantContext.TenantId;
            var settings = await _concessions.GetMenuSettings(tenantId);

            // Block online orders when closed (season/event/hours), manually paused, or at capacity.
            var orderingStatus = await EvaluateOrderingStatus(settings);
            if (!orderingStatus.OpenNow)
                return new ApiResponses().BadRequestResult(orderingStatus.Reason ?? "Online ordering is closed right now. Please check back when the track is open.");

            var (lines, subtotal, cartError) = await ResolveCartLines(tenantId, req.Items);
            if (cartError is not null) return new ApiResponses().BadRequestResult(cartError);

            // Tips are only honored when the tenant has enabled them; otherwise force 0 server-side.
            var tipsEnabled = settings?.TipsEnabled ?? false;
            var pricesIncludeTax = settings?.PricesIncludeTax ?? false;
            var tipCents = tipsEnabled ? Math.Max(0, req.TipCents) : 0;
            var taxCents = lines.Sum(l => l.TaxCents);
            var total = subtotal + (pricesIncludeTax ? 0 : taxCents) + tipCents;
            if (total < 50) return new ApiResponses().BadRequestResult("Order total must be at least 50 cents.");

            if (_tenantContext.Tenant.StripeChargeMode == "direct" && string.IsNullOrEmpty(_tenantContext.Tenant.StripeConnectAccountId))
                return new ApiResponses().BadRequestResult(
                    "This track is set to charge on its own Stripe account but hasn't connected one yet.");
            var connectedAccountId = DirectConnectedAccountId();

            // ── Store credit: the signed-in rider burns their own balance (server-resolved by
            // user id, capped at the total). Same pre-generated-id/redeem-first shape as the POS.
            Services.Repositories.Data.CreditData.TenantCreditAccount? creditAccount = null;
            var creditApplied = 0;
            if (req.CreditCents > 0)
            {
                creditAccount = await _credit.GetAccountForUser(tenantId, userId);
                if (creditAccount is not null && creditAccount.BalanceCents > 0)
                    creditApplied = Math.Min(Math.Min(req.CreditCents, creditAccount.BalanceCents), total);
            }
            var due = total - creditApplied;
            if (due > 0 && due < 50)
                return new ApiResponses().BadRequestResult(
                    "Less than 50 cents would be left to charge after credit. Adjust the order or keep the credit for next time.");
            var saleId = Guid.NewGuid();
            if (creditApplied > 0 &&
                !await _credit.TryAdjust(creditAccount!.Id, tenantId, -creditApplied, "redeem", "concession_sale", saleId, null, userId))
            {
                return new ApiResponses().BadRequestResult("Your credit balance just changed. Reload and try again.");
            }

            var sale = new ConcessionSale
            {
                Id = saleId,
                TenantId = tenantId,
                Status = "pending",
                FulfillmentStatus = "active",
                SubtotalCents = subtotal,
                TipCents = tipCents,
                TaxCents = taxCents,
                PricesIncludeTax = pricesIncludeTax,
                TotalCents = total,
                CreditAppliedCents = creditApplied,
                CreditAccountId = creditApplied > 0 ? creditAccount!.Id : null,
                PaymentMethod = string.IsNullOrEmpty(connectedAccountId) ? "stripe" : "stripe_direct",
                StripeConnectedAccountId = connectedAccountId,
                OrderChannel = "online",
                PurchaserUserId = userId,
                PurchaserEmail = user.Email,
                PurchaserName = $"{user.FirstName} {user.LastName}".Trim(),
            };
            try
            {
                sale.Id = await _concessions.CreateSale(sale);
                await _concessions.CreateSaleLines(sale.Id, lines);
            }
            catch
            {
                await _credit.ReverseRedeem(tenantId, "concession_sale", saleId, "order could not be created");
                throw;
            }
            // Fully covered by credit: nothing to charge; settle it straight onto the cook screen
            // (no ledger entry: no money moved, and the credit's value was booked when funded).
            if (due == 0)
            {
                await _concessions.MarkSalePaid(sale.Id);
                var orderNumber = await _concessions.NextOrderNumber(tenantId);
                await _concessions.SetOrderNumber(sale.Id, orderNumber);
                // An order of nothing but grab-and-go items has no cook-screen line to bump, so settle
                // it as ready here; anything with a real prep line stays 'active' for the kitchen.
                await _concessions.RecomputeSaleFulfillment(sale.Id, tenantId);
                try { await _concessions.DepleteInventoryForSale(sale.Id, tenantId); } catch { /* inventory is best-effort */ }
                await NotifyLowStock(tenantId);
                // Nothing to cook (all grab-and-go) means no bump will ever fire the ready text, so send
                // it here. No-ops for any order that still has prep lines.
                await NotifyIfReady(sale.Id);
                return new ApiResponses().OkResult(new ConcessionSaleResponse
                {
                    SaleId = sale.Id,
                    TotalCents = total,
                    CreditAppliedCents = creditApplied,
                    DueCents = 0,
                    Status = "paid",
                    OrderNumber = orderNumber,
                });
            }

            var metadata = new Dictionary<string, string>
            {
                ["tenant_id"] = tenantId.ToString(),
                ["sale_kind"] = "concession",
                ["concession_sale_id"] = sale.Id.ToString(),
            };
            PaymentIntentCreated intent;
            try
            {
                intent = await _payments.CreatePaymentIntentAsync(due, "usd", metadata, user.Email,
                    connectedAccountId: connectedAccountId, ct: ct);
            }
            catch (InvalidOperationException ex)
            {
                await _concessions.MarkSaleFailed(sale.Id);
                await _credit.ReverseRedeem(tenantId, "concession_sale", sale.Id, "payment could not start");
                return new ApiResponses().BadRequestResult(ex.Message);
            }
            await _concessions.SetSalePaymentIntentId(sale.Id, intent.IntentId);

            return new ApiResponses().OkResult(new ConcessionSaleResponse
            {
                SaleId = sale.Id,
                ClientSecret = intent.ClientSecret,
                PaymentIntentId = intent.IntentId,
                TotalCents = total,
                CreditAppliedCents = creditApplied,
                DueCents = due,
                Status = "pending",
            });
        }

        // The rider's own recent orders + live status (they poll this after paying to see their number
        // and whether it's ready).
        [Authorize]
        [HttpGet("MyOrders")]
        public async Task<IActionResult> MyOrders()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!Guid.TryParse(User.FindFirst("UserId")?.Value, out var userId))
                return new ApiResponses().BadRequestResult("Invalid token.");
            var orders = await _concessions.ListOrdersForPurchaser(_tenantContext.TenantId, userId);
            return new ApiResponses().OkResult(orders.Select(o => new
            {
                saleId = o.Id,
                status = o.Status,
                fulfillmentStatus = o.FulfillmentStatus,
                orderNumber = o.OrderNumber,
                totalCents = o.TotalCents,
                createdAtUtc = DateTime.SpecifyKind(o.CreatedAt, DateTimeKind.Utc),
            }).ToList());
        }

        // ── Helpers ─────────────────────────────────────────────────────────────
        private async Task<List<ConcessionProductResponse>> Hydrate(List<ConcessionProduct> products, bool activeOnly)
        {
            if (products.Count == 0) return new();
            var variantsByProduct = await _concessions.ListVariantsForProducts(products.Select(p => p.Id));
            var sold = await _concessions.SumSoldVariants(
                variantsByProduct.Values.SelectMany(v => v).Select(v => v.Id));
            var groupsByProduct = await BuildGroupResponses(_tenantContext.TenantId, products.Select(p => p.Id), activeOnly);
            var defaultsByProduct = await _concessions.ListProductDefaultOptionLinks(products.Select(p => p.Id));

            // Simple (no active variant) products track stock at the product level, so gather their sold counts.
            var baseProductIds = products
                .Where(p =>
                {
                    var vs = variantsByProduct.GetValueOrDefault(p.Id, new());
                    return !(activeOnly ? vs.Where(v => v.IsActive) : vs).Any();
                })
                .Select(p => p.Id).ToList();
            var soldByProduct = await _concessions.SumSoldProducts(baseProductIds);
            var today = TenantToday();

            var responses = new List<ConcessionProductResponse>();
            foreach (var p in products)
            {
                var variants = variantsByProduct.GetValueOrDefault(p.Id, new());
                if (activeOnly) variants = variants.Where(v => v.IsActive).ToList();
                var r = ToProductResponse(p, variants, sold, groupsByProduct.GetValueOrDefault(p.Id, new()));
                r.DefaultModifierOptionIds = defaultsByProduct.GetValueOrDefault(p.Id, new());
                ApplyProductAvailability(r, p, variants, soldByProduct, today);
                responses.Add(r);
            }
            return responses;
        }

        // product_id -> its modifier groups (with options), respecting display order. activeOnly hides
        // inactive groups/options from the cashier; the admin view passes false to see everything.
        private async Task<Dictionary<Guid, List<ConcessionModifierGroupResponse>>> BuildGroupResponses(
            Guid tenantId, IEnumerable<Guid> productIds, bool activeOnly = false)
        {
            var ids = productIds.ToList();
            if (ids.Count == 0) return new();
            var links = await _concessions.ListProductGroupLinks(ids);   // product -> ordered group ids
            if (links.Count == 0) return new();
            var groupsById = (await _concessions.ListModifierGroups(tenantId, activeOnly)).ToDictionary(g => g.Id);
            var optionsByGroup = (await _concessions.ListOptionsForGroups(groupsById.Keys, activeOnly))
                .GroupBy(o => o.GroupId).ToDictionary(g => g.Key, g => g.ToList());

            var result = new Dictionary<Guid, List<ConcessionModifierGroupResponse>>();
            foreach (var (productId, groupIds) in links)
            {
                var list = new List<ConcessionModifierGroupResponse>();
                foreach (var gid in groupIds)
                {
                    if (!groupsById.TryGetValue(gid, out var g)) continue;   // inactive / filtered out
                    list.Add(new ConcessionModifierGroupResponse
                    {
                        Id = g.Id,
                        Name = g.Name,
                        MinSelect = g.MinSelect,
                        MaxSelect = g.MaxSelect,
                        IsRequired = g.IsRequired,
                        SortOrder = g.SortOrder,
                        IsActive = g.IsActive,
                        Options = optionsByGroup.GetValueOrDefault(g.Id, new()).Select(o => new ConcessionModifierGroupResponse.OptionItem
                        {
                            Id = o.Id,
                            Name = o.Name,
                            PriceDeltaCents = o.PriceDeltaCents,
                            SortOrder = o.SortOrder,
                            IsActive = o.IsActive,
                        }).ToList(),
                    });
                }
                if (list.Count > 0) result[productId] = list;
            }
            return result;
        }

        // Keeps a product's station + modifier-group links honest: drops any id that isn't this
        // tenant's, so a client can't point a product at another tenant's station/group.
        private async Task<(Guid? stationId, List<Guid> groupIds)> ValidateStationAndGroups(
            Guid tenantId, Guid? stationId, List<Guid> groupIds)
        {
            Guid? validStation = null;
            if (stationId.HasValue)
            {
                var stations = await _concessions.ListStations(tenantId, activeOnly: false);
                if (stations.Any(s => s.Id == stationId.Value)) validStation = stationId;
            }
            var groups = await _concessions.ListModifierGroups(tenantId, activeOnly: false);
            var valid = groupIds.Where(id => groups.Any(g => g.Id == id)).Distinct().ToList();
            return (validStation, valid);
        }

        // Keeps a product's category honest: returns the id only if it's one of this tenant's categories,
        // otherwise null (uncategorized), so a client can't point a product at another tenant's category.
        // Keeps a category's board honest: returns the id only if it's one of this tenant's menu
        // boards, otherwise null (all boards), so a client can't point a category at another tenant's board.
        private async Task<Guid?> ValidateMenuBoard(Guid tenantId, Guid? menuBoardId)
        {
            if (!menuBoardId.HasValue) return null;
            var boards = await _concessions.ListMenuBoards(tenantId, activeOnly: false);
            return boards.Any(b => b.Id == menuBoardId.Value) ? menuBoardId : null;
        }

        private async Task<Guid?> ValidateCategory(Guid tenantId, Guid? categoryId)
        {
            if (!categoryId.HasValue) return null;
            var cats = await _concessions.ListCategories(tenantId, activeOnly: false);
            return cats.Any(c => c.Id == categoryId.Value) ? categoryId : null;
        }

        // Keeps a product's tax category honest: returns the id only if it's one of this tenant's tax
        // categories, otherwise null (falls back to the tenant default at checkout).
        private async Task<Guid?> ValidateTaxCategory(Guid tenantId, Guid? taxCategoryId)
        {
            if (!taxCategoryId.HasValue) return null;
            var cats = await _concessions.ListTaxCategories(tenantId);
            return cats.Any(c => c.Id == taxCategoryId.Value) ? taxCategoryId : null;
        }

        // Keeps default option ids honest: only options that belong to the item's assigned groups are
        // stored as defaults (so a client can't default-select an arbitrary or another tenant's option).
        private async Task<List<Guid>> ValidDefaultOptions(List<Guid> groupIds, List<Guid> requested)
        {
            if (requested.Count == 0 || groupIds.Count == 0) return new();
            var valid = (await _concessions.ListOptionsForGroups(groupIds, activeOnly: false)).Select(o => o.Id).ToHashSet();
            return requested.Where(valid.Contains).Distinct().ToList();
        }

        // The connected account a 'direct' tenant's card-present sales run on (null = platform mode).
        private string? DirectConnectedAccountId()
            => _tenantContext.Tenant.StripeChargeMode == "direct"
                ? _tenantContext.Tenant.StripeConnectAccountId
                : null;

        // Replicates CounterController.EnsureTerminalLocation: returns the Terminal Location id for
        // the given account (the tenant's connected account in 'direct' mode, else the platform
        // account), lazily creating it from the tenant address, or null if the address is incomplete.
        private async Task<string?> EnsureTerminalLocation(string? connectedAccountId, CancellationToken ct)
        {
            var tenant = _tenantContext.Tenant;
            var direct = !string.IsNullOrEmpty(connectedAccountId);
            var existing = direct ? tenant.StripeConnectedTerminalLocationId : tenant.StripeTerminalLocationId;
            if (!string.IsNullOrWhiteSpace(existing))
                return existing;
            if (string.IsNullOrWhiteSpace(tenant.AddressLine) || string.IsNullOrWhiteSpace(tenant.City)
                || string.IsNullOrWhiteSpace(tenant.Country) || string.IsNullOrWhiteSpace(tenant.PostalCode))
            {
                return null;
            }
            string locationId;
            try
            {
                locationId = await _payments.CreateTerminalLocationAsync(
                    tenant.DisplayName,
                    new TerminalLocationAddress(
                        Line1: tenant.AddressLine, City: tenant.City,
                        Country: tenant.Country, PostalCode: tenant.PostalCode, State: tenant.Region),
                    connectedAccountId,
                    ct);
            }
            catch (InvalidOperationException) { return null; }
            if (direct) await _tenants.SetStripeConnectedTerminalLocationId(_tenantContext.TenantId, locationId);
            else await _tenants.SetStripeTerminalLocationId(_tenantContext.TenantId, locationId);
            return locationId;
        }

        private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        private static string? VariantLabel(ConcessionVariant v)
        {
            var parts = new[] { v.Size, v.Color }.Where(s => !string.IsNullOrWhiteSpace(s));
            var label = string.Join(" / ", parts);
            return string.IsNullOrWhiteSpace(label) ? null : label;
        }

        private static ConcessionProductResponse ToProductResponse(
            ConcessionProduct p, List<ConcessionVariant> variants, Dictionary<Guid, int>? sold = null,
            List<ConcessionModifierGroupResponse>? modifierGroups = null)
        {
            sold ??= new();
            return new ConcessionProductResponse
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                CategoryId = p.CategoryId,
                CategoryName = p.CategoryName,
                CategorySortOrder = p.CategorySortOrder,
                PriceCents = p.PriceCents,
                ImageUrl = p.ImageUrl,
                ShowInCarousel = p.ShowInCarousel,
                IsActive = p.IsActive,
                SortOrder = p.SortOrder,
                StationId = p.StationId,
                RequiresPrep = p.RequiresPrep,
                TaxCategoryId = p.TaxCategoryId,
                Inventory = p.Inventory,
                ComboAvailable = p.ComboAvailable,
                Variants = variants.Select(v => ToVariantResponse(v, sold.GetValueOrDefault(v.Id, 0))).ToList(),
                ModifierGroups = modifierGroups ?? new(),
            };
        }

        // Builds the shared combo definition (tiers + slots with options) for the build modal + admin editor.
        private async Task<ConcessionComboConfigResponse> BuildComboConfig(Guid tenantId)
        {
            var tiers = await _concessions.GetComboTiers(tenantId);
            var slots = await _concessions.GetComboSlots(tenantId);
            return new ConcessionComboConfigResponse
            {
                Tiers = tiers.Select(t => new ConcessionComboConfigResponse.Tier
                {
                    Id = t.Id, Name = t.Name, SizeLabel = t.SizeLabel, PriceCents = t.PriceCents, SortOrder = t.SortOrder,
                }).ToList(),
                Slots = slots.Select(s => new ConcessionComboConfigResponse.Slot
                {
                    Id = s.Id, Name = s.Name, IsRequired = s.IsRequired, SortOrder = s.SortOrder,
                    Options = s.Options.Select(o => new ConcessionComboConfigResponse.Option
                    {
                        Id = o.Id, ComponentProductId = o.ComponentProductId,
                        ComponentName = o.ComponentName ?? "", IsDefault = o.IsDefault, SortOrder = o.SortOrder,
                    }).ToList(),
                }).ToList(),
            };
        }

        // Fills the product-level availability fields (SoldOut / ManuallySoldOut / Remaining) that need a
        // sold-count lookup + "today", so the POS, menu board, and online menu can show / block 86'd items.
        private static void ApplyProductAvailability(
            ConcessionProductResponse r, ConcessionProduct p, List<ConcessionVariant> activeVariants,
            Dictionary<Guid, int> soldByProduct, DateTime today)
        {
            var manually86 = p.SoldOutDate.HasValue && p.SoldOutDate.Value.Date == today;
            r.ManuallySoldOut = manually86;
            if (activeVariants.Count == 0)
            {
                var soldP = soldByProduct.GetValueOrDefault(p.Id, 0);
                r.Remaining = p.Inventory.HasValue ? Math.Max(0, p.Inventory.Value - soldP) : -1;
                r.SoldOut = manually86 || (p.Inventory.HasValue && r.Remaining <= 0);
            }
            else
            {
                r.Remaining = -1;   // stock tracked per variant, not at the product level
                r.SoldOut = manually86 || r.Variants.Count > 0 && r.Variants.All(v => v.Remaining == 0);
            }
        }

        private static ConcessionVariantResponse ToVariantResponse(ConcessionVariant v, int sold) => new()
        {
            Id = v.Id,
            ProductId = v.ProductId,
            Size = v.Size,
            Color = v.Color,
            PriceCents = v.PriceCents,
            ImageUrl = v.ImageUrl,
            Inventory = v.Inventory,
            Sold = sold,
            Remaining = v.Inventory.HasValue ? Math.Max(0, v.Inventory.Value - sold) : -1,
            IsActive = v.IsActive,
            SortOrder = v.SortOrder,
        };

        private static ConcessionCategoryResponse ToCategoryResponse(ConcessionCategory c) => new()
        {
            Id = c.Id,
            Name = c.Name,
            SortOrder = c.SortOrder,
            IsActive = c.IsActive,
            MenuBoardId = c.MenuBoardId,
        };

        private static ConcessionMenuBoardResponse ToMenuBoardResponse(ConcessionMenuBoard b) => new()
        {
            Id = b.Id,
            Name = b.Name,
            SortOrder = b.SortOrder,
            IsActive = b.IsActive,
        };

        private static ConcessionDisplayResponse ToDisplayResponse(ConcessionDisplay d) => new()
        {
            Id = d.Id,
            PairCode = d.PairCode,
            StateJson = d.StateJson,
            TipCents = d.TipCents,
        };

        private static ConcessionMenuPromoResponse ToMenuPromoResponse(ConcessionMenuPromo p) => new()
        {
            Id = p.Id,
            Title = p.Title,
            Subtitle = p.Subtitle,
            ImageUrl = p.ImageUrl,
            MenuBoardId = p.MenuBoardId,
            SortOrder = p.SortOrder,
            IsActive = p.IsActive,
        };

        private static ConcessionStationResponse ToStationResponse(ConcessionStation s) => new()
        {
            Id = s.Id,
            Name = s.Name,
            SortOrder = s.SortOrder,
            IsActive = s.IsActive,
        };

        private static ConcessionModifierGroupResponse.OptionItem ToOptionResponse(ConcessionModifierOption o) => new()
        {
            Id = o.Id,
            Name = o.Name,
            PriceDeltaCents = o.PriceDeltaCents,
            SortOrder = o.SortOrder,
            IsActive = o.IsActive,
        };

        private static ConcessionModifierGroupResponse ToGroupResponse(
            ConcessionModifierGroup g, List<ConcessionModifierOption> options) => new()
        {
            Id = g.Id,
            Name = g.Name,
            MinSelect = g.MinSelect,
            MaxSelect = g.MaxSelect,
            IsRequired = g.IsRequired,
            SortOrder = g.SortOrder,
            IsActive = g.IsActive,
            Options = options.Select(ToOptionResponse).ToList(),
        };

        // Resolves a submitted cart into priced sale lines (product/variant/modifiers/station snapshots
        // + inventory check). Shared by the staff counter sale and the rider online order. Returns an
        // error message instead of throwing so the caller surfaces it.
        private async Task<(List<ConcessionSaleLine> lines, int subtotal, string? error)> ResolveCartLines(
            Guid tenantId, List<ConcessionSaleRequest.SaleLine> items)
        {
            var requested = items.Where(i => i.Quantity > 0).ToList();
            if (requested.Count == 0) return (new(), 0, "Cart is empty.");

            // Inventory is reserved per variant across every line that references it.
            var requestedByVariant = requested.Where(i => i.VariantId.HasValue)
                .GroupBy(i => i.VariantId!.Value)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
            // Simple (no-variant) items reserve product-level stock across all of their lines.
            var requestedByProduct = requested.Where(i => !i.VariantId.HasValue)
                .GroupBy(i => i.ProductId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
            var today = TenantToday();

            // Tax config: each item's rate comes from its tax category (or the tenant default). Inclusive
            // pricing backs tax out of the listed price; otherwise it's added on top. Loaded once here so
            // each line carries a frozen rate + tax snapshot.
            var pricesIncludeTax = (await _concessions.GetMenuSettings(tenantId))?.PricesIncludeTax ?? false;
            var taxCats = await _concessions.ListTaxCategories(tenantId);
            var defaultRateBps = taxCats.FirstOrDefault(c => c.IsDefault)?.RateBps ?? 0;
            var rateByCategory = taxCats.ToDictionary(c => c.Id, c => c.RateBps);

            var lines = new List<ConcessionSaleLine>();
            var subtotal = 0;
            foreach (var item in requested)
            {
                var product = await _concessions.GetProduct(item.ProductId, tenantId);
                if (product is null || !product.IsActive)
                    return (new(), 0, "One of the selected items isn't available.");
                // Manually 86'd for today (cleared automatically tomorrow).
                if (product.SoldOutDate.HasValue && product.SoldOutDate.Value.Date == today)
                    return (new(), 0, $"\"{product.Name}\" is sold out.");

                var activeVariants = (await _concessions.ListVariants(product.Id)).Where(v => v.IsActive).ToList();
                int basePrice;
                ConcessionVariant? variant = null;
                string? label = null;

                if (activeVariants.Count > 0)
                {
                    if (!item.VariantId.HasValue) return (new(), 0, $"Choose an option for \"{product.Name}\".");
                    variant = activeVariants.FirstOrDefault(v => v.Id == item.VariantId.Value);
                    if (variant is null) return (new(), 0, $"That option isn't available for \"{product.Name}\".");
                    if (variant.Inventory.HasValue)
                    {
                        var sold = await _concessions.SumSoldVariant(variant.Id);
                        var remaining = variant.Inventory.Value - sold;
                        if (requestedByVariant[variant.Id] > remaining)
                        {
                            var qual = VariantLabel(variant) is { } l ? $"{product.Name} ({l})" : product.Name;
                            return (new(), 0, remaining <= 0 ? $"\"{qual}\" is sold out." : $"Only {remaining} of \"{qual}\" left.");
                        }
                    }
                    basePrice = variant.PriceCents ?? product.PriceCents;
                    label = VariantLabel(variant);
                }
                else
                {
                    basePrice = product.PriceCents;
                    if (product.Inventory.HasValue)
                    {
                        var soldP = await _concessions.SumSoldProduct(product.Id);
                        var remaining = product.Inventory.Value - soldP;
                        if (requestedByProduct.GetValueOrDefault(product.Id, 0) > remaining)
                            return (new(), 0, remaining <= 0
                                ? $"\"{product.Name}\" is sold out."
                                : $"Only {remaining} of \"{product.Name}\" left.");
                    }
                }

                var (mods, modifierDelta, modError) = await ResolveModifiers(tenantId, product, item.ModifierOptionIds);
                if (modError is not null) return (new(), 0, modError);

                var unitPrice = basePrice + modifierDelta;
                var line = new ConcessionSaleLine
                {
                    ProductId = product.Id,
                    VariantId = variant?.Id,
                    StationId = product.StationId,
                    NameSnapshot = product.Name,
                    VariantLabel = label,
                    UnitPriceCents = unitPrice,
                    Quantity = item.Quantity,
                    LineTotalCents = unitPrice * item.Quantity,
                    Notes = Blank(item.Notes),
                    Modifiers = mods,
                    // Grab-and-go: snapshot the setting and land the line already 'ready' so it neither
                    // shows on the cook screen nor holds the order in "Preparing" waiting for a bump.
                    RequiresPrep = product.RequiresPrep,
                    PrepStatus = product.RequiresPrep ? "queued" : "ready",
                };

                // "Make it a combo": layer a size tier + side/drink children onto the entree line.
                if (item.ComboTierId.HasValue)
                {
                    if (!product.ComboAvailable) return (new(), 0, $"\"{product.Name}\" isn't available as a combo.");
                    var comboError = await ApplyCombo(tenantId, line, product, item, today);
                    if (comboError is not null) return (new(), 0, comboError);
                }

                // Snapshot the tax on the (now final, combo-inclusive) line total. Combo child lines are
                // $0 and keep the default 0 tax.
                var rateBps = product.TaxCategoryId.HasValue && rateByCategory.TryGetValue(product.TaxCategoryId.Value, out var r)
                    ? r : defaultRateBps;
                line.TaxRateBps = rateBps;
                line.TaxCents = ComputeLineTax(line.LineTotalCents, rateBps, pricesIncludeTax);

                lines.Add(line);
                subtotal += line.LineTotalCents;
            }
            return (lines, subtotal, null);
        }

        // Tax on a line's total at a basis-point rate. Exclusive: added on top (round(base * rate)).
        // Inclusive: the portion already inside the listed price (base - base/(1+rate)). Half-up rounding.
        private static int ComputeLineTax(int baseCents, int rateBps, bool pricesIncludeTax)
        {
            if (rateBps <= 0 || baseCents <= 0) return 0;
            if (pricesIncludeTax)
                return baseCents - (int)Math.Round(baseCents * 10000.0 / (10000.0 + rateBps), MidpointRounding.AwayFromZero);
            return (int)Math.Round(baseCents * rateBps / 10000.0, MidpointRounding.AwayFromZero);
        }

        // Validates a line's selected modifier options against the product's configured groups
        // (min/max/required), then returns the frozen modifier snapshots + the per-unit price delta.
        private async Task<(List<ConcessionSaleLineModifier> mods, int delta, string? error)> ResolveModifiers(
            Guid tenantId, ConcessionProduct product, List<Guid> optionIds)
        {
            var groupIds = await _concessions.GetProductGroupIds(product.Id);
            if (groupIds.Count == 0)
            {
                if (optionIds.Count > 0) return (new(), 0, $"\"{product.Name}\" doesn't take options.");
                return (new(), 0, null);
            }
            var groups = (await _concessions.ListModifierGroups(tenantId, activeOnly: true))
                .Where(g => groupIds.Contains(g.Id)).ToList();
            var validGroupIds = groups.Select(g => g.Id).ToHashSet();
            var optionById = (await _concessions.ListOptionsForGroups(groupIds, activeOnly: true))
                .Where(o => validGroupIds.Contains(o.GroupId))
                .ToDictionary(o => o.Id);

            var selected = optionIds.Distinct().ToList();
            foreach (var oid in selected)
                if (!optionById.ContainsKey(oid))
                    return (new(), 0, $"An option for \"{product.Name}\" is no longer available.");

            var countByGroup = selected.GroupBy(id => optionById[id].GroupId)
                .ToDictionary(g => g.Key, g => g.Count());
            foreach (var g in groups)
            {
                var count = countByGroup.GetValueOrDefault(g.Id, 0);
                if (g.IsRequired && count == 0) return (new(), 0, $"Choose {g.Name} for \"{product.Name}\".");
                if (count < g.MinSelect) return (new(), 0, $"Choose at least {g.MinSelect} for {g.Name} on \"{product.Name}\".");
                if (g.MaxSelect.HasValue && count > g.MaxSelect.Value)
                    return (new(), 0, $"Choose at most {g.MaxSelect} for {g.Name} on \"{product.Name}\".");
            }

            var groupNameById = groups.ToDictionary(g => g.Id, g => g.Name);
            var mods = new List<ConcessionSaleLineModifier>();
            var delta = 0;
            foreach (var oid in selected)
            {
                var o = optionById[oid];
                mods.Add(new ConcessionSaleLineModifier
                {
                    ModifierOptionId = o.Id,
                    GroupNameSnapshot = groupNameById.GetValueOrDefault(o.GroupId, ""),
                    OptionNameSnapshot = o.Name,
                    PriceDeltaCentsSnapshot = o.PriceDeltaCents,
                });
                delta += o.PriceDeltaCents;
            }
            return (mods, delta, null);
        }

        // "Make it a combo": layers the chosen size tier + one component per slot onto the entree line.
        // The tier adds its upcharge; each slot's included (default) option is covered by the tier, and a
        // substitution is charged max(0, its price - the included price) at the tier's size. The chosen
        // components become $0 child lines (sized to the tier) so the cook screen routes them and their
        // recipes deplete inventory. Mutates `entree` in place; returns an error message or null.
        private async Task<string?> ApplyCombo(
            Guid tenantId, ConcessionSaleLine entree, ConcessionProduct product,
            ConcessionSaleRequest.SaleLine item, DateTime today)
        {
            var tiers = await _concessions.GetComboTiers(tenantId);
            var tier = tiers.FirstOrDefault(t => t.Id == item.ComboTierId!.Value);
            if (tier is null) return "Choose a combo size.";

            var slots = await _concessions.GetComboSlots(tenantId);
            var chosenBySlot = item.ComboSelections.GroupBy(s => s.SlotId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.OptionId).Distinct().ToList());

            var children = new List<ConcessionSaleLine>();
            var upcharge = tier.PriceCents;

            foreach (var slot in slots)
            {
                chosenBySlot.TryGetValue(slot.Id, out var chosen);
                chosen ??= new();
                if (chosen.Count == 0)
                {
                    if (slot.IsRequired) return $"Choose {slot.Name} for the combo.";
                    continue;
                }
                if (chosen.Count > 1) return $"Choose only one option for {slot.Name}.";

                var opt = slot.Options.FirstOrDefault(o => o.Id == chosen[0]);
                if (opt is null) return $"That choice isn't available for {slot.Name}.";

                var component = await _concessions.GetProduct(opt.ComponentProductId, tenantId);
                if (component is null || !component.IsActive) return $"A {slot.Name} option isn't available right now.";
                if (component.SoldOutDate.HasValue && component.SoldOutDate.Value.Date == today)
                    return $"\"{component.Name}\" is sold out.";

                // Resolve this component's variant + price at the tier's size (fall back to the base item).
                var (variantId, sizeLabel, chosenPrice) = await ResolveTierSize(component, tier.SizeLabel);

                // Included (default) option price at the same size is the baseline; premium subs add the
                // difference, cheaper subs add nothing.
                var included = slot.Options.FirstOrDefault(o => o.IsDefault);
                var includedPrice = chosenPrice;
                if (included is not null && included.ComponentProductId != component.Id)
                {
                    var inc = await _concessions.GetProduct(included.ComponentProductId, tenantId);
                    if (inc is not null) (_, _, includedPrice) = await ResolveTierSize(inc, tier.SizeLabel);
                }
                upcharge += Math.Max(0, chosenPrice - includedPrice);

                children.Add(new ConcessionSaleLine
                {
                    ProductId = component.Id,
                    VariantId = variantId,
                    StationId = component.StationId,
                    NameSnapshot = component.Name,
                    VariantLabel = sizeLabel,
                    UnitPriceCents = 0,
                    Quantity = item.Quantity,
                    LineTotalCents = 0,
                    // A grab-and-go component (bagged chips as the combo side) skips the cook screen too.
                    RequiresPrep = component.RequiresPrep,
                    PrepStatus = component.RequiresPrep ? "queued" : "ready",
                });
            }

            entree.IsCombo = true;
            entree.ComboTier = tier.Name;
            entree.UnitPriceCents += upcharge;
            entree.LineTotalCents = entree.UnitPriceCents * item.Quantity;
            entree.Children = children;
            return null;
        }

        // Finds a component's active variant whose size matches the tier's size label (case-insensitive),
        // returning its id/label/price; falls back to the base product when there's no size or match.
        private async Task<(Guid? variantId, string? sizeLabel, int price)> ResolveTierSize(
            ConcessionProduct component, string? tierSize)
        {
            if (!string.IsNullOrWhiteSpace(tierSize))
            {
                var variant = (await _concessions.ListVariants(component.Id))
                    .FirstOrDefault(v => v.IsActive && string.Equals(v.Size, tierSize, StringComparison.OrdinalIgnoreCase));
                if (variant is not null)
                    return (variant.Id, VariantLabel(variant), variant.PriceCents ?? component.PriceCents);
            }
            return (null, null, component.PriceCents);
        }

        // Cash concession sale: record a 'sale' ledger entry as payment_method='cash' so it flows
        // into the worker's cash reconciliation (sold_by_user_id attribution + cash method).
        //
        // The tenant already holds the cash from the drawer, so net_to_tenant must NOT be the gross
        // (that would sweep the same money into their next platform payout — paying it out twice).
        // Mirror CounterController's cash convention: net = -RidepassCut, i.e. the tenant owes the
        // platform only its cut. With the current F&B policy of a zero cut this nets to 0; if an F&B
        // cut is ever introduced this stays correct without another change here.
        private async Task WriteCashLedger(ConcessionSale sale)
        {
            try
            {
                // Store credit never moves money (its value was booked when funded), so the entry
                // records only the cash the drawer actually took.
                var collected = sale.TotalCents - sale.CreditAppliedCents;
                var calc = await _feeCalculator.Calculate(sale.TenantId, collected, 0, 0, DateTime.UtcNow);
                await _ledger.Insert(new TenantLedgerEntry
                {
                    TenantId = sale.TenantId,
                    EntryKind = "sale",
                    SourceKind = "concession",
                    SourceId = sale.Id,
                    OccurredAtUtc = DateTime.UtcNow,
                    GrossCents = collected,
                    StripeFeeCents = 0,
                    RidepassCutCents = calc.RidepassCutCents,
                    NetToTenantCents = -calc.RidepassCutCents,
                    PaymentMethod = "cash",
                    SoldByUserId = sale.SoldByUserId,   // cashier, for worker reconciliation
                    Memo = "Cash sale, tenant owes service charge",
                });
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505") { /* idempotent */ }
        }

        // Negative-mirror of the original sale entry (matches the platform refund convention).
        private async Task WriteRefundLedger(ConcessionSale sale, Guid? refundedBy, string? authorizedByName = null)
        {
            var entry = await _ledger.GetSaleEntryForSource(sale.TenantId, "concession", sale.Id);
            if (entry is null) return;
            var memo = string.IsNullOrWhiteSpace(authorizedByName)
                ? "Food & Beverage refund"
                : $"Food & Beverage refund (approved by {authorizedByName})";
            try
            {
                await _ledger.Insert(new TenantLedgerEntry
                {
                    TenantId = sale.TenantId,
                    EntryKind = "refund",
                    SourceKind = "concession",
                    SourceId = sale.Id,
                    OccurredAtUtc = DateTime.UtcNow,
                    GrossCents = -entry.GrossCents,
                    StripeFeeCents = -entry.StripeFeeCents,
                    RidepassCutCents = -entry.RidepassCutCents,
                    NetToTenantCents = -entry.NetToTenantCents,
                    StripePaymentIntentId = entry.StripePaymentIntentId,
                    PaymentMethod = sale.PaymentMethod,
                    SoldByUserId = refundedBy,   // who issued the refund, for worker reconciliation
                    Memo = memo,
                });
            }
            // The unique refund-per-source index makes a concurrent double-refund's loser idempotent
            // (the money + status change already happened); swallow it like the other ledger inserts.
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505") { }
        }
    }
}
