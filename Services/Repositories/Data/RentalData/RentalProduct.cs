namespace Services.Repositories.Data.RentalData
{
    public class RentalProduct
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public string? ImageUrl { get; set; }
        public int DailyRateCents { get; set; }
        public int DepositCents { get; set; }
        // 'pool' = inventory_pool count of identical units; 'per_item' = rental_item rows.
        public string TrackingKind { get; set; } = "pool";
        public int? InventoryPool { get; set; }
        public bool RequiresWaiver { get; set; } = true;
        public int RiderPaidServiceChargeBps { get; set; } = 10000;
        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; } = 100;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class RentalItem
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid ProductId { get; set; }
        public string Label { get; set; } = null!;
        public string? Serial { get; set; }
        public string? Notes { get; set; }
        // 'available' | 'maintenance' | 'retired'
        public string Status { get; set; } = "available";
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
