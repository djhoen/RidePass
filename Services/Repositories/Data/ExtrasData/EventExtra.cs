namespace Services.Repositories.Data.ExtrasData
{
    public class EventExtraProduct
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public string? ImageUrl { get; set; }
        // Free-form. Defaults: 'camping' | 'parking' | 'pit_vehicle'. Custom labels
        // allowed (the Vue picker slugifies user input client-side).
        public string Kind { get; set; } = null!;
        public int PriceCents { get; set; }
        public int RiderPaidServiceChargeBps { get; set; } = 10000;
        public bool RequiresWaiver { get; set; }
        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; } = 100;
        // Optional cutoff: when set and in the past, the product stops selling.
        public DateTime? ExpiresAt { get; set; }
        // Tenant-wide cap on units sold across every event + variant. Null = unlimited.
        public int? Inventory { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class EventExtraVariant
    {
        public Guid Id { get; set; }
        public Guid ProductId { get; set; }
        // All three nullable — a product can use any subset (or none, in which
        // case the product itself is the only "variant" via legacy buy path).
        public string? Size { get; set; }
        public string? Color { get; set; }
        public string? Gender { get; set; }
        public string? Sku { get; set; }
        // Freeform tier label (e.g. "Standard" / "Premium") and per-variant
        // description, both optional.
        public string? Tier { get; set; }
        public string? Description { get; set; }
        // Null = inherit from the parent product.
        public int? PriceCents { get; set; }
        public int? Inventory { get; set; }       // null = unlimited
        public string? ImageUrl { get; set; }
        public int SortOrder { get; set; } = 100;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class EventExtraEligibility
    {
        public Guid EventId { get; set; }
        public Guid ProductId { get; set; }
        // null = unlimited at this event; > 0 = capped to that number.
        public int? Inventory { get; set; }
    }

    public class EventExtraPurchase
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        // Null when sold as merchandise via the counter (no event attachment).
        // Set when purchased through an event-detail flow.
        public Guid? EventId { get; set; }
        public Guid ProductId { get; set; }
        public Guid? PurchaserUserId { get; set; }
        public string PurchaserEmail { get; set; } = null!;
        public string PurchaserName { get; set; } = null!;
        public Guid? WaiverSignatureId { get; set; }
        public int Quantity { get; set; } = 1;
        public int UnitPriceCentsFrozen { get; set; }
        public int AmountCents { get; set; }
        public int ServiceChargeCents { get; set; }
        public string? StripePaymentIntentId { get; set; }
        public Guid RedemptionToken { get; set; }
        public string Status { get; set; } = "pending";
        public DateTime? RedeemedAtUtc { get; set; }
        public Guid? RedeemedByUserId { get; set; }
        public string? CancelledReason { get; set; }
        public Guid? CancelledByUserId { get; set; }
        public DateTime? CancelledAt { get; set; }
        public string? RefundNote { get; set; }
        public Guid? SoldByUserId { get; set; }
        public string PaymentMethod { get; set; } = "stripe";
        // Variant link (null when the product has no variants — legacy "single SKU" path).
        // Frozen attribute strings let historical reads survive variant edits / deletes.
        public Guid? VariantId { get; set; }
        public string? SizeAtPurchase { get; set; }
        public string? ColorAtPurchase { get; set; }
        public string? GenderAtPurchase { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
