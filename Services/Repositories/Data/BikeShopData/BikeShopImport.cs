namespace Services.Repositories.Data.BikeShopData
{
    // Parsed + validated CSV import payload (the controller does the parsing/validation; the
    // repository turns this into one transaction of inserts). Rows sharing a product name
    // become one product with N variants.

    public class ShopImportProduct
    {
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public string? Brand { get; set; }
        public string? CategoryName { get; set; }
        public string? SupplierName { get; set; }
        public List<ShopImportVariant> Variants { get; set; } = new();
    }

    public class ShopImportVariant
    {
        public string? Sku { get; set; }
        public string? Barcode { get; set; }
        public string? Size { get; set; }
        public string? Color { get; set; }
        public string? Gender { get; set; }
        /// <summary>Manufacturer part number. The identity a distributor export leads with, and
        /// the second thing an update matches on after the barcode.</summary>
        public string? Mpn { get; set; }
        /// <summary>The manufacturer's own name for the part. The one field an import can put into
        /// the cross-tenant parts library, which is why it is separate from the product name.</summary>
        public string? ManufacturerName { get; set; }
        public string? VendorPartNumber { get; set; }
        public int? SalePriceCents { get; set; }
        public int? CostCents { get; set; }
        public int? MsrpCents { get; set; }
        public int? DailyRateCents { get; set; }
        public int DepositCents { get; set; }
        public string TrackingKind { get; set; } = "pool";
        public int Stock { get; set; }
        public int? LowStockThreshold { get; set; }
    }

    /// <summary>Column keys the import understands. Used to record which ones a FILE actually
    /// carried, so an update writes only those.</summary>
    public static class ShopImportColumn
    {
        public const string Description = "description";
        public const string Brand = "brand";
        public const string Sku = "sku";
        public const string Barcode = "barcode";
        public const string Mpn = "mpn";
        public const string VendorPartNumber = "vendorpartnumber";
        public const string ManufacturerName = "manufacturername";
        public const string Size = "size";
        public const string Color = "color";
        public const string Gender = "gender";
        public const string Price = "price";
        public const string Cost = "cost";
        public const string Msrp = "msrp";
        public const string DailyRate = "dailyrate";
        public const string Deposit = "deposit";
        public const string LowStock = "lowstock";
    }

    public class ShopImportOptions
    {
        /// <summary>
        /// Update rows that already exist instead of rejecting the whole file. Off by default:
        /// a first import into a live catalog should still refuse to silently rewrite it.
        /// </summary>
        public bool UpdateExisting { get; set; }

        /// <summary>
        /// Which <see cref="ShopImportColumn"/> keys the file actually carried. An update writes
        /// ONLY these, which is what makes a cost-only distributor refresh safe: a file with no
        /// price column must never blank the shop's own retail prices, and an absent column is a
        /// very different thing from an empty cell.
        /// </summary>
        public HashSet<string> PresentColumns { get; set; } = new();

        /// <summary>
        /// Where any manufacturer names in this batch came from, one of
        /// <see cref="Services.BikeShop.ManufacturerNameSource"/>. Defaults to 'import' because a
        /// CSV a shop uploaded is the ordinary case; the distributor sync overrides it with its own
        /// slug so licensed content can be told apart from the shop's own data and kept out of the
        /// shared library. Set by the CALLER, never by the file.
        /// </summary>
        public string ManufacturerNameSource { get; set; } = Services.BikeShop.ManufacturerNameSource.Import;
    }

    public class ShopImportResult
    {
        public int Products { get; set; }
        public int Variants { get; set; }
        /// <summary>Rows that matched something already in the catalog and were updated in place.</summary>
        public int ProductsUpdated { get; set; }
        public int VariantsUpdated { get; set; }
        public List<string> NewCategories { get; set; } = new();
        public List<string> NewSuppliers { get; set; } = new();
    }
}
