namespace Services.Repositories.Data.BikeShopData
{
    // Inventory reporting rows (Lightspeed parity: valuation, sales/COGS/margin, dead stock).
    // Money sums are bigint-safe longs; the queries do the aggregation so the API ships totals,
    // not raw movements.

    public class ShopValuationRow
    {
        public Guid VariantId { get; set; }
        public string ProductName { get; set; } = "";
        public string? VariantLabel { get; set; }
        public string? Sku { get; set; }
        public string? CategoryName { get; set; }
        public string TrackingKind { get; set; } = "pool";
        public int OnHand { get; set; }               // pool cached count; serialized = owned units
        public int? CostCents { get; set; }
        public int? SalePriceCents { get; set; }
        public long CostValueCents { get; set; }      // serialized sums per-unit acquired costs
        public long RetailValueCents { get; set; }
    }

    public class ShopSalesReportRow
    {
        public string ProductName { get; set; } = "";
        public string? VariantLabel { get; set; }
        public string? Sku { get; set; }
        public int Units { get; set; }
        public long RevenueCents { get; set; }        // goods net of discounts, pre-tax
        public long CogsCents { get; set; }           // frozen cost snapshots, else current cost
    }

    public class ShopDeadStockRow
    {
        public Guid VariantId { get; set; }
        public string ProductName { get; set; } = "";
        public string? VariantLabel { get; set; }
        public string? Sku { get; set; }
        public int OnHand { get; set; }
        public long CostValueCents { get; set; }
        public DateTime? LastSoldAt { get; set; }
    }
}
