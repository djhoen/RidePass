namespace Services.Repositories.Data.ProductData
{
    public class Product
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public decimal Price { get; set; }
        public int ProductTypeId { get; set; }
        public string? ImageUrl { get; set; }
        public bool IsActive { get; set; }
        public bool IsAddOn { get; set; }
        public bool IsBundle { get; set; }
        public bool IsShipable { get; set; }
        public DateTime? BeginDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string? Sku { get; set; }
        public int? StatusId { get; set; }
    }

    public enum ProductType
    {
        Digital = 1,
        Physical = 2,
        Service = 3
    }

    public class ProductBundleItem
    {
        public int Id { get; set; }
        public int ParentProductId { get; set; }
        public int ProductId { get; set; }
        public int Qty { get; set; }
    }

    public class ProductOffer
    {
        public int Id { get; set; }
        public string? ShortDescription { get; set; }
        public string? LongDescription { get; set; }
        public int ProductId { get; set; }
        public int OfferProductId { get; set; }
        public bool IsActive { get; set; }
        public Product? OfferProduct { get; set; }
    }
}
