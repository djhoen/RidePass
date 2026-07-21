namespace Services.Repositories.Data.BikeShopData
{
    /// <summary>One page of the catalog plus the totals for the WHOLE filtered set.</summary>
    public class ShopCatalogPage
    {
        public List<ShopProductWithVariants> Rows { get; set; } = new();
        /// <summary>Matching products across all pages.</summary>
        public int Total { get; set; }
        public ShopCatalogTotals Totals { get; set; } = new();
    }

    /// <summary>
    /// Header aggregates for the catalog list. Deliberately computed over the whole filtered set,
    /// not the visible page: "what is my stock worth" is a question about the filter, not about
    /// the 25 rows that happen to be on screen.
    /// </summary>
    public class ShopCatalogTotals
    {
        /// <summary>Available quantity valued at sale price. bigint: a full catalog easily exceeds int cents.</summary>
        public long StockRetailValueCents { get; set; }
        /// <summary>Available quantity valued at last cost paid. The other half of the margin picture.</summary>
        public long StockCostValueCents { get; set; }
        /// <summary>Matching products with at least one pool variant at or below its low-stock threshold.</summary>
        public int LowStockCount { get; set; }
        /// <summary>Units ordered from suppliers but not yet received (POs in 'ordered' or 'partial').</summary>
        public int UnitsOnPo { get; set; }
    }
}
