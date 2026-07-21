namespace Services.Repositories.Data.BikeShopData
{
    /// <summary>One pool variant that has fallen to or below its reorder point, with everything the
    /// buyer needs to raise a purchase order: the supplier, the vendor part number, current on-hand,
    /// and a suggested quantity that tops it back up to the reorder level.</summary>
    public class ShopReorderRow
    {
        public Guid VariantId { get; set; }
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = "";
        public string? VariantLabel { get; set; }
        public string? Sku { get; set; }
        public string? VendorPartNumber { get; set; }
        public Guid? SupplierId { get; set; }
        public string? SupplierName { get; set; }
        public int Available { get; set; }
        public int ReorderPoint { get; set; }
        public int? ReorderLevel { get; set; }
        public int? CostCents { get; set; }
        /// <summary>Top up to the reorder level (or one past the point if no level is set), at least 1.</summary>
        public int SuggestedQty { get; set; }
    }
}
