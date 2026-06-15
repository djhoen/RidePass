using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Payments;
using Services.Repositories.Data.ConcessionData;
using Services.Repositories.Interfaces;
using Services.Storage;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Concession;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    /// <summary>
    /// Concessions / store: a standalone in-person storefront (food, drink, swag) the
    /// cashier rings up via the mobile tap-to-pay app. Admin endpoints (CatalogManage)
    /// manage the catalog; the SalesCounter endpoints back the cashier app: list items and
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
        private readonly ITenantContext _tenantContext;

        public ConcessionController(
            IConcessionRepository concessions,
            IPaymentProvider payments,
            IImageStorage imageStorage,
            ITenantRepository tenants,
            ITenantContext tenantContext)
        {
            _concessions = concessions;
            _payments = payments;
            _imageStorage = imageStorage;
            _tenants = tenants;
            _tenantContext = tenantContext;
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
            var p = new ConcessionProduct
            {
                TenantId = _tenantContext.TenantId,
                Name = req.Name.Trim(),
                Description = Blank(req.Description),
                Category = req.Category.Trim().ToLowerInvariant(),
                PriceCents = req.PriceCents,
                ImageUrl = Blank(req.ImageUrl),
                IsActive = req.IsActive,
                SortOrder = req.SortOrder,
            };
            p.Id = await _concessions.CreateProduct(p);
            return new ApiResponses().OkResult(ToProductResponse(p, new()));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPut("Products/{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpsertConcessionProductRequest req)
        {
            var existing = await _concessions.GetProduct(id, _tenantContext.TenantId);
            if (existing is null) return new ApiResponses().NotFoundResult("Item not found.");
            existing.Name = req.Name.Trim();
            existing.Description = Blank(req.Description);
            existing.Category = req.Category.Trim().ToLowerInvariant();
            existing.PriceCents = req.PriceCents;
            existing.ImageUrl = Blank(req.ImageUrl);
            existing.IsActive = req.IsActive;
            existing.SortOrder = req.SortOrder;
            await _concessions.UpdateProduct(existing);
            var variants = await _concessions.ListVariants(existing.Id);
            var sold = await _concessions.SumSoldVariants(variants.Select(v => v.Id));
            return new ApiResponses().OkResult(ToProductResponse(existing, variants, sold));
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

        // ── Cashier app (SalesCounter) ──────────────────────────────────────────
        // Active items + variants for the cashier to ring up.
        [Authorize(Policy = TenantPermissions.Policy.SalesCounter)]
        [HttpGet("Items")]
        public async Task<IActionResult> Items()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!_tenantContext.Tenant.ConcessionsEnabled)
                return new ApiResponses().OkResult(new List<ConcessionProductResponse>());
            var products = await _concessions.ListProducts(_tenantContext.TenantId, activeOnly: true);
            return new ApiResponses().OkResult(await Hydrate(products, activeOnly: true));
        }

        // Anonymous-buyer card-present sale. Server computes the authoritative total from the
        // catalog (never trusts a client amount), records the sale pending, and creates a
        // card-present PaymentIntent the cashier app confirms on the reader. The payment
        // webhook flips the sale to paid and writes the ledger entry.
        [Authorize(Policy = TenantPermissions.Policy.SalesCounter)]
        [HttpPost("Sale")]
        public async Task<IActionResult> CreateSale([FromBody] ConcessionSaleRequest req, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!_tenantContext.Tenant.ConcessionsEnabled)
                return new ApiResponses().BadRequestResult("Concessions aren't enabled for this track.");

            // Dedupe by (product, variant), sum quantities. A null variant is its own bucket.
            var items = req.Items
                .Where(i => i.Quantity > 0)
                .GroupBy(i => new { i.ProductId, i.VariantId })
                .Select(g => new { g.Key.ProductId, g.Key.VariantId, Quantity = g.Sum(x => x.Quantity) })
                .ToList();
            if (items.Count == 0) return new ApiResponses().BadRequestResult("Cart is empty.");

            var lines = new List<ConcessionSaleLine>();
            var subtotal = 0;
            foreach (var item in items)
            {
                var product = await _concessions.GetProduct(item.ProductId, _tenantContext.TenantId);
                if (product is null || !product.IsActive)
                    return new ApiResponses().BadRequestResult("One of the selected items isn't available.");

                var activeVariants = (await _concessions.ListVariants(product.Id)).Where(v => v.IsActive).ToList();
                int unitPrice;
                ConcessionVariant? variant = null;
                string? label = null;

                if (activeVariants.Count > 0)
                {
                    if (!item.VariantId.HasValue)
                        return new ApiResponses().BadRequestResult($"Choose an option for \"{product.Name}\".");
                    variant = activeVariants.FirstOrDefault(v => v.Id == item.VariantId.Value);
                    if (variant is null)
                        return new ApiResponses().BadRequestResult($"That option isn't available for \"{product.Name}\".");
                    if (variant.Inventory.HasValue)
                    {
                        var sold = await _concessions.SumSoldVariant(variant.Id);
                        var remaining = variant.Inventory.Value - sold;
                        if (item.Quantity > remaining)
                        {
                            var qual = VariantLabel(variant) is { } l ? $"{product.Name} ({l})" : product.Name;
                            return new ApiResponses().BadRequestResult(remaining <= 0
                                ? $"\"{qual}\" is sold out."
                                : $"Only {remaining} of \"{qual}\" left.");
                        }
                    }
                    unitPrice = variant.PriceCents ?? product.PriceCents;
                    label = VariantLabel(variant);
                }
                else
                {
                    unitPrice = product.PriceCents;
                }

                lines.Add(new ConcessionSaleLine
                {
                    ProductId = product.Id,
                    VariantId = variant?.Id,
                    NameSnapshot = product.Name,
                    VariantLabel = label,
                    UnitPriceCents = unitPrice,
                    Quantity = item.Quantity,
                    LineTotalCents = unitPrice * item.Quantity,
                });
                subtotal += unitPrice * item.Quantity;
            }

            var total = subtotal;   // all-in pricing: no tax or added service charge
            if (total < 50) return new ApiResponses().BadRequestResult("Sale total must be at least 50 cents.");

            var locationId = await EnsureTerminalLocation(ct);
            if (locationId is null)
                return new ApiResponses().BadRequestResult(
                    "Cannot take card-present payments until the track's address is filled in (Settings -> General).");

            Guid? soldBy = Guid.TryParse(User.FindFirst("UserId")?.Value, out var uid) ? uid : null;
            var sale = new ConcessionSale
            {
                TenantId = _tenantContext.TenantId,
                Status = "pending",
                SubtotalCents = subtotal,
                TotalCents = total,
                SoldByUserId = soldBy,
            };
            sale.Id = await _concessions.CreateSale(sale);
            await _concessions.CreateSaleLines(sale.Id, lines);

            var metadata = new Dictionary<string, string>
            {
                ["tenant_id"] = _tenantContext.TenantId.ToString(),
                ["sale_kind"] = "concession",
                ["concession_sale_id"] = sale.Id.ToString(),
            };
            PaymentIntentCreated intent;
            try
            {
                intent = await _payments.CreateCardPresentPaymentIntentAsync(total, "usd", locationId, metadata, null, ct);
            }
            catch (InvalidOperationException ex)
            {
                await _concessions.MarkSaleFailed(sale.Id);
                return new ApiResponses().BadRequestResult(ex.Message);
            }
            await _concessions.SetSalePaymentIntentId(sale.Id, intent.IntentId);

            return new ApiResponses().OkResult(new ConcessionSaleResponse
            {
                SaleId = sale.Id,
                ClientSecret = intent.ClientSecret,
                PaymentIntentId = intent.IntentId,
                TotalCents = total,
            });
        }

        // ── Helpers ─────────────────────────────────────────────────────────────
        private async Task<List<ConcessionProductResponse>> Hydrate(List<ConcessionProduct> products, bool activeOnly)
        {
            if (products.Count == 0) return new();
            var variantsByProduct = await _concessions.ListVariantsForProducts(products.Select(p => p.Id));
            var sold = await _concessions.SumSoldVariants(
                variantsByProduct.Values.SelectMany(v => v).Select(v => v.Id));
            var responses = new List<ConcessionProductResponse>();
            foreach (var p in products)
            {
                var variants = variantsByProduct.GetValueOrDefault(p.Id, new());
                if (activeOnly) variants = variants.Where(v => v.IsActive).ToList();
                responses.Add(ToProductResponse(p, variants, sold));
            }
            return responses;
        }

        // Replicates CounterController.EnsureTerminalLocation: returns the tenant's Stripe
        // Terminal location, lazily creating it from the tenant address, or null if the
        // address isn't complete enough for Stripe.
        private async Task<string?> EnsureTerminalLocation(CancellationToken ct)
        {
            var tenant = _tenantContext.Tenant;
            if (!string.IsNullOrWhiteSpace(tenant.StripeTerminalLocationId))
                return tenant.StripeTerminalLocationId;
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
                    ct);
            }
            catch (InvalidOperationException) { return null; }
            await _tenants.SetStripeTerminalLocationId(_tenantContext.TenantId, locationId);
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
            ConcessionProduct p, List<ConcessionVariant> variants, Dictionary<Guid, int>? sold = null)
        {
            sold ??= new();
            return new ConcessionProductResponse
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                Category = p.Category,
                PriceCents = p.PriceCents,
                ImageUrl = p.ImageUrl,
                IsActive = p.IsActive,
                SortOrder = p.SortOrder,
                Variants = variants.Select(v => ToVariantResponse(v, sold.GetValueOrDefault(v.Id, 0))).ToList(),
            };
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
    }
}
