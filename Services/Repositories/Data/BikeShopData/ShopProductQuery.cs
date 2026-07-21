namespace Services.Repositories.Data.BikeShopData
{
    /// <summary>
    /// Filter/paging criteria for the shop catalog list. The catalog is the one screen that grows
    /// without bound (a real shop carries thousands of SKUs), so it is queried as a page rather
    /// than loaded whole.
    /// </summary>
    public class ShopProductQuery
    {
        /// <summary>
        /// Free text, matched case-insensitively against product name and brand, and against any
        /// variant's SKU or barcode. One box for type-or-scan, mirroring how shop staff search.
        /// </summary>
        public string? Search { get; set; }

        public Guid? CategoryId { get; set; }
        public Guid? SupplierId { get; set; }

        /// <summary>Hide inactive products.</summary>
        public bool ActiveOnly { get; set; }

        /// <summary>
        /// Purpose filters. The retail catalog passes Sellable=true and the rental fleet passes
        /// Rentable=true; a product flagged both shows up in both lists, each in its own context.
        /// Null leaves the flag unfiltered.
        /// </summary>
        public bool? Sellable { get; set; }
        public bool? Rentable { get; set; }

        /// <summary>Only products with a variant at or below its low-stock threshold.</summary>
        public bool LowStockOnly { get; set; }

        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 25;
    }
}
