namespace Services.Repositories.Data.BikeShopData
{
    /// <summary>
    /// One additional photo on a shop product's gallery (Script0230). The product's own
    /// <c>image_url</c> remains the cover; this holds the rest. A row's ImageUrl can be the
    /// same blob as the cover (the admin's "Make cover" copies the url), which is why
    /// deleting a row checks for other references before deleting the file.
    /// </summary>
    public class ShopProductImage
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid ProductId { get; set; }
        public string ImageUrl { get; set; } = null!;
        public string? Caption { get; set; }
        public int SortOrder { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
