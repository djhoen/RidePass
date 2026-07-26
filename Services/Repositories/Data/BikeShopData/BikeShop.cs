namespace Services.Repositories.Data.BikeShopData
{
    // The unified bike shop catalog + inventory. See docs/bike-shop.md. Price and stock live on the
    // variant, not the product; serialized units are ShopItem rows; every stock change is a
    // ShopStockMovement, with ShopVariant.StockOnHand a cache of the pool count.

    public class ShopCategory
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = null!;
        public Guid? ParentId { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class ShopSupplier
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = null!;
        public string? ContactName { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Notes { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class ShopProduct
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid? CategoryId { get; set; }
        public Guid? SupplierId { get; set; }
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public string? Brand { get; set; }
        public string? ImageUrl { get; set; }
        public bool IsSellable { get; set; } = true;
        /// <summary>Listed in the online store. Distinct from IsSellable (sellable at the counter).</summary>
        public bool IsPublished { get; set; } = true;
        public bool IsRentable { get; set; }
        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class ShopVariant
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid ProductId { get; set; }
        public string? Sku { get; set; }
        /// <summary>The barcode as typed or imported. Kept verbatim because it is what prints on
        /// the shop's own label; <see cref="Gtin14"/> is the key anything MATCHES on.</summary>
        public string? Barcode { get; set; }
        /// <summary>
        /// Barcode normalised to a 14-digit GTIN, or null when it isn't a valid one. Derived from
        /// Barcode by the repository on every write, so no caller can set one without the other.
        /// This is what a register scan compares against: see Services.BikeShop.Gtin for why the
        /// raw string is not comparable.
        /// </summary>
        public string? Gtin14 { get; set; }
        public string? Size { get; set; }
        public string? Color { get; set; }
        public string? Gender { get; set; }
        public int? SalePriceCents { get; set; }
        /// <summary>Manufacturer's suggested retail price, shown as a compare-at. Null = none.</summary>
        public int? MsrpCents { get; set; }
        public int? DailyRateCents { get; set; }
        public int DepositCents { get; set; }
        public int? CostCents { get; set; }
        /// <summary>Manufacturer part number (vendor part number is VendorPartNumber).</summary>
        public string? Mpn { get; set; }
        /// <summary>
        /// The manufacturer's own name for this part, as printed on the box.
        ///
        /// This is the ONLY field fed into the cross-tenant platform_part library, and the reason
        /// it exists: the product's own Name is what THIS shop calls it, is tenant-authored free
        /// text, and must never be shown to another tenant. Leave this null and the part simply
        /// isn't contributed.
        /// </summary>
        public string? ManufacturerName { get; set; }
        /// <summary>
        /// Where ManufacturerName came from: one of Services.BikeShop.ManufacturerNameSource.
        /// Decides whether that name may be pooled into the shared library, because a distributor's
        /// licensed content is not ours to redistribute even though it IS a genuine manufacturer
        /// name. Null means unknown, which is treated as not poolable.
        /// </summary>
        public string? ManufacturerNameSource { get; set; }
        public string TrackingKind { get; set; } = "pool";   // pool | serialized
        // Authoritative for pool variants only. For serialized, availability is the count of
        // ShopItems with status 'available'; this stays 0.
        public int StockOnHand { get; set; }
        // NULL = no low-stock alerting. When on-hand falls to/below this, managers get one alert
        // per low episode (LowStockNotifiedAt is the de-dupe stamp, cleared when stock recovers).
        public int? LowStockThreshold { get; set; }
        public DateTime? LowStockNotifiedAt { get; set; }
        // Reorder planning (pool variants). ReorderPoint = the on-hand level that triggers a reorder;
        // ReorderLevel = the "order up to" target used to suggest the quantity. Null = not planned.
        public int? ReorderPoint { get; set; }
        public int? ReorderLevel { get; set; }
        /// <summary>The supplier's own part number, printed on POs so they can find it.</summary>
        public string? VendorPartNumber { get; set; }
        /// <summary>This variant's entry in the shared parts library, once a scan has matched the
        /// two up. Identity link only: price, cost and stock are and stay this tenant's. Null until
        /// the barcode has been scanned at least once, and back to null if that shared entry is
        /// ever purged for licensing reasons.</summary>
        public Guid? PlatformPartId { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>A variant plus its resolved on-hand: the cached count for pool, the available-item
    /// count for serialized. This is the number the catalog and register should show.</summary>
    public class ShopVariantWithStock : ShopVariant
    {
        public int AvailableCount { get; set; }
    }

    /// <summary>
    /// A variant resolved from a scanned code, carrying the product name so the register can name
    /// the line without a second lookup.
    /// </summary>
    public class ShopScanMatch : ShopVariantWithStock
    {
        public string ProductName { get; set; } = null!;
    }

    /// <summary>A product with its variants, for the catalog view.</summary>
    public class ShopProductWithVariants : ShopProduct
    {
        public List<ShopVariantWithStock> Variants { get; set; } = new();
    }

    public class ShopItem
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid VariantId { get; set; }
        public string Label { get; set; } = null!;
        public string? Serial { get; set; }
        public string? Notes { get; set; }
        public string Status { get; set; } = "available";   // available|rented_out|sold|maintenance|retired
        public int? AcquiredCostCents { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class ShopStockMovement
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid VariantId { get; set; }
        public Guid? ItemId { get; set; }
        public int Delta { get; set; }
        public string Reason { get; set; } = null!;   // receive|sale|rental_out|rental_return|repair_consume|adjustment|stocktake|transfer
        public string? ReferenceKind { get; set; }
        public Guid? ReferenceId { get; set; }
        public int? UnitCostCents { get; set; }
        public string? Note { get; set; }
        public Guid? CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class ShopPurchaseOrder
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid? SupplierId { get; set; }
        public string? Reference { get; set; }
        public string Status { get; set; } = "open";   // open|ordered|partial|received|cancelled
        public string? Notes { get; set; }
        public DateTime? OrderedAt { get; set; }
        public DateTime? ExpectedAt { get; set; }
        public DateTime? ReceivedAt { get; set; }
        public Guid? CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class ShopPoLine
    {
        public Guid Id { get; set; }
        public Guid PoId { get; set; }
        public Guid VariantId { get; set; }
        public int QuantityOrdered { get; set; }
        public int QuantityReceived { get; set; }
        public int UnitCostCents { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class ShopPurchaseOrderWithLines : ShopPurchaseOrder
    {
        public List<ShopPoLine> Lines { get; set; } = new();
    }

    // Physical stock takes (pool variants only; serialized units are trued by item status).
    public class ShopStockCount
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Status { get; set; } = "open";   // open | completed | cancelled
        public string? Notes { get; set; }
        public Guid? StartedByUserId { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
    }

    public class ShopStockCountLine
    {
        public Guid Id { get; set; }
        public Guid CountId { get; set; }
        public Guid VariantId { get; set; }
        public int ExpectedQty { get; set; }
        public int? CountedQty { get; set; }
        // Display fields (joined for the count sheet).
        public string ProductName { get; set; } = "";
        public string? VariantLabel { get; set; }
        public string? Sku { get; set; }
    }

    public class ShopStockCountWithLines : ShopStockCount
    {
        public List<ShopStockCountLine> Lines { get; set; } = new();
    }
}
