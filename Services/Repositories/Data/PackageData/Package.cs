namespace Services.Repositories.Data.PackageData
{
    /// <summary>A bundled package product (e.g. "Find Your Ride"): a coached session +
    /// day admission + a bike + gear, sold at day-type tiers.</summary>
    public class PackageProduct
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Slug { get; set; }
        public string? Summary { get; set; }
        public string? Description { get; set; }
        public string? HeroImageUrl { get; set; }
        public bool LandingPublished { get; set; }
        public bool IncludesDayTicket { get; set; } = true;
        public string DayTicketEventTypeCode { get; set; } = "open_ride";
        public int? CoachingMinutes { get; set; }
        public string? CoachingLabel { get; set; }
        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; }
        public DateTime? ValidFromDate { get; set; }
        public DateTime? ValidToDate { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public List<PackageTier> Tiers { get; set; } = new();
        public List<PackageSessionSlot> Slots { get; set; } = new();
        public List<PackageItem> Items { get; set; } = new();
    }

    /// <summary>A priced day-type option (Midweek / Weekend / Afternoon / 3-Pack).</summary>
    public class PackageTier
    {
        public Guid Id { get; set; }
        public Guid PackageId { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int PriceCents { get; set; }
        public string DayScope { get; set; } = "any";       // any | weekday | weekend
        public bool AfternoonOnly { get; set; }
        public int SessionCount { get; set; } = 1;
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
    }

    /// <summary>A bookable coached session time (weekday vs weekend).</summary>
    public class PackageSessionSlot
    {
        public Guid Id { get; set; }
        public Guid PackageId { get; set; }
        public Guid TenantId { get; set; }
        public string DayScope { get; set; } = "any";       // any | weekday | weekend
        public TimeSpan StartTime { get; set; }
        public bool IsAfternoon { get; set; }
        public int Capacity { get; set; } = 8;
        public Guid? InstructorId { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
    }

    /// <summary>An included rental item (the bike, or a gear piece), resolved to a rentable variant.</summary>
    public class PackageItem
    {
        public Guid Id { get; set; }
        public Guid PackageId { get; set; }
        public Guid TenantId { get; set; }
        public string ItemType { get; set; } = "gear";      // bike | gear
        public Guid VariantId { get; set; }
        public int Quantity { get; set; } = 1;
        public int SortOrder { get; set; }
        // Hydrated for display (name/size/deposit) from shop_variant; not stored on package_item.
        public string? VariantName { get; set; }
        public string? VariantLabel { get; set; }
        public int DepositCents { get; set; }
        // For bike items: the selectable size variants (siblings of the same product). Empty for gear.
        public List<PackageBikeSizeOption> SizeOptions { get; set; } = new();
    }

    /// <summary>A selectable bike size for a package bike item (a rentable sibling variant).</summary>
    public class PackageBikeSizeOption
    {
        public Guid VariantId { get; set; }
        public string Label { get; set; } = string.Empty;
        public int DepositCents { get; set; }
    }

    /// <summary>One composed package sale (a gate ticket + a rental + a session).</summary>
    public class PackagePurchase
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid PackageId { get; set; }
        public Guid? TierId { get; set; }
        public Guid? BuyerUserId { get; set; }
        public string? BuyerName { get; set; }
        public string? BuyerEmail { get; set; }
        public DateTime RideDate { get; set; }
        public DateTime? SessionStartAt { get; set; }
        public Guid? SlotId { get; set; }
        public Guid? InstructorId { get; set; }
        public string Status { get; set; } = "pending";
        public int SubtotalCents { get; set; }
        public int TaxCents { get; set; }
        public int TotalCents { get; set; }
        public int DepositCents { get; set; }
        public int ServiceChargeCents { get; set; }
        public string? PaymentIntentId { get; set; }
        public string? DepositIntentId { get; set; }
        public string? StripeConnectedAccountId { get; set; }
        public int? OrderNumber { get; set; }
        public Guid ReceiptToken { get; set; }
        public Guid? EventTicketPurchaseId { get; set; }
        public Guid? ShopRentalId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? PaidAt { get; set; }
    }
}
