using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Extras
{
    public class UpsertExtraProductRequest
    {
        [Required, MaxLength(140)]
        public string Name { get; set; } = null!;

        [MaxLength(2000)]
        public string? Description { get; set; }

        [MaxLength(500)]
        public string? ImageUrl { get; set; }

        // 'camping' / 'parking' / 'pit_vehicle' are common defaults; tenants may
        // pass any other slug for custom kinds.
        [Required, MaxLength(60), RegularExpression("^[a-z0-9_-]+$")]
        public string Kind { get; set; } = null!;

        [Range(0, 10_000_000)]
        public int PriceCents { get; set; }

        [Range(0, 10000)]
        public int RiderPaidServiceChargeBps { get; set; } = 10000;

        public bool RequiresWaiver { get; set; } = false;
        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; } = 100;
        // Optional cutoff. UTC. Past expiry → not sellable.
        public DateTime? ExpiresAt { get; set; }
        // Tenant-wide cap. Null = unlimited.
        [Range(0, 1_000_000)]
        public int? Inventory { get; set; }
    }

    public class ExtraProductResponse
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public string? ImageUrl { get; set; }
        public string Kind { get; set; } = null!;
        public int PriceCents { get; set; }
        public int RiderPaidServiceChargeBps { get; set; }
        public bool RequiresWaiver { get; set; }
        public bool IsActive { get; set; }
        public int SortOrder { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public int? Inventory { get; set; }
        public int Sold { get; set; }
        public int Remaining { get; set; }      // -1 if unlimited
        // Empty for legacy single-SKU products.
        public List<ExtraVariantResponse> Variants { get; set; } = new();
    }

    public class ExtraVariantResponse
    {
        public Guid Id { get; set; }
        public Guid ProductId { get; set; }
        public string? Size { get; set; }
        public string? Color { get; set; }
        public string? Gender { get; set; }
        public string? Sku { get; set; }
        public string? Tier { get; set; }
        public string? Description { get; set; }
        // Effective price = PriceCents ?? product.PriceCents.
        public int? PriceCents { get; set; }
        public int? Inventory { get; set; }
        public int Sold { get; set; }
        public int Remaining { get; set; }      // -1 if unlimited
        public string? ImageUrl { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
    }

    public class UpsertExtraVariantRequest
    {
        [MaxLength(40)] public string? Size { get; set; }
        [MaxLength(40)] public string? Color { get; set; }
        [MaxLength(40)] public string? Gender { get; set; }
        [MaxLength(80)] public string? Sku { get; set; }
        [MaxLength(60)] public string? Tier { get; set; }
        [MaxLength(500)] public string? Description { get; set; }
        [Range(0, 10_000_000)]
        public int? PriceCents { get; set; }
        [Range(0, 100000)]
        public int? Inventory { get; set; }
        [MaxLength(500)]
        public string? ImageUrl { get; set; }
        public int SortOrder { get; set; } = 100;
        public bool IsActive { get; set; } = true;
    }

    public class UpdateExtrasEnabledRequest
    {
        public bool Enabled { get; set; }
    }

    // ── Per-event eligibility (set/edit on the EventDialog) ──────────────────
    public class EventExtraEligibilityInput
    {
        [Required] public Guid ProductId { get; set; }
        [Range(1, 100000)]
        public int? Inventory { get; set; }   // null = unlimited
    }

    public class EventExtraEligibilityResponse
    {
        public Guid ProductId { get; set; }
        public string Name { get; set; } = null!;
        public string Kind { get; set; } = null!;
        public int PriceCents { get; set; }
        public int? Inventory { get; set; }
        public int Sold { get; set; }
        public int Remaining { get; set; }    // -1 if unlimited
        public bool RequiresWaiver { get; set; }
    }

    // ── Rider purchase ───────────────────────────────────────────────────────
    public class BuyExtrasRequest
    {
        [Required] public Guid EventId { get; set; }
        [Required, MinLength(1)]
        public List<BuyExtrasItem> Items { get; set; } = new();
    }

    public class BuyExtrasItem
    {
        [Required] public Guid ProductId { get; set; }
        [Range(1, 50)] public int Quantity { get; set; } = 1;
        // Required when the product has any active variants; ignored otherwise.
        public Guid? VariantId { get; set; }
    }

    public class BuyExtrasResponse
    {
        public List<Guid> PurchaseIds { get; set; } = new();
        public string ClientSecret { get; set; } = null!;
        public int AmountCents { get; set; }
        public int RiderServiceChargeCents { get; set; }
    }

    public class MyExtraResponse
    {
        public Guid Id { get; set; }
        public Guid RedemptionToken { get; set; }
        // Null for counter-sale merch (no event attachment).
        public Guid? EventId { get; set; }
        public string? EventTitle { get; set; }
        public DateTime? EventStartsAtUtc { get; set; }
        public string ProductName { get; set; } = null!;
        public string Kind { get; set; } = null!;
        public int Quantity { get; set; }
        public int AmountCents { get; set; }
        public string Status { get; set; } = null!;
        public DateTime CreatedAtUtc { get; set; }
    }
}
