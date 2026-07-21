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
        public int? SalePriceCents { get; set; }
        public int? CostCents { get; set; }
        public int? DailyRateCents { get; set; }
        public int DepositCents { get; set; }
        public string TrackingKind { get; set; } = "pool";
        public int Stock { get; set; }
        public int? LowStockThreshold { get; set; }
    }

    public class ShopImportResult
    {
        public int Products { get; set; }
        public int Variants { get; set; }
        public List<string> NewCategories { get; set; } = new();
        public List<string> NewSuppliers { get; set; } = new();
    }
}
