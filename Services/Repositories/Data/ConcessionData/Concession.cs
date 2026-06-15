namespace Services.Repositories.Data.ConcessionData
{
    public class ConcessionProduct
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public string Category { get; set; } = "other";   // food | drink | swag | other
        public int PriceCents { get; set; }
        public string? ImageUrl { get; set; }
        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class ConcessionVariant
    {
        public Guid Id { get; set; }
        public Guid ProductId { get; set; }
        public string? Size { get; set; }
        public string? Color { get; set; }
        public int? PriceCents { get; set; }   // null = use product price
        public string? ImageUrl { get; set; }
        public int? Inventory { get; set; }     // null = unlimited
        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class ConcessionSale
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Status { get; set; } = "pending";   // pending | paid | failed | refunded
        public int SubtotalCents { get; set; }
        public int TotalCents { get; set; }
        public string? StripePaymentIntentId { get; set; }
        public Guid? SoldByUserId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? PaidAt { get; set; }
    }

    public class ConcessionSaleLine
    {
        public Guid Id { get; set; }
        public Guid SaleId { get; set; }
        public Guid? ProductId { get; set; }
        public Guid? VariantId { get; set; }
        public string NameSnapshot { get; set; } = null!;
        public string? VariantLabel { get; set; }
        public int UnitPriceCents { get; set; }
        public int Quantity { get; set; }
        public int LineTotalCents { get; set; }
    }
}
