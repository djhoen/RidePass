namespace webapi.Controllers.API.Data.Package
{
    /// <summary>Public / admin package payload (landing + structure).</summary>
    public class PackageResponse
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Slug { get; set; }
        public string? Summary { get; set; }
        public string? Description { get; set; }
        public string? HeroImageUrl { get; set; }
        public bool LandingPublished { get; set; }
        public bool IncludesDayTicket { get; set; }
        public int? CoachingMinutes { get; set; }
        public string? CoachingLabel { get; set; }
        public bool IsActive { get; set; }
        public int SortOrder { get; set; }
        public List<PackageTierResponse> Tiers { get; set; } = new();
        public List<PackageSlotResponse> Slots { get; set; } = new();
        public List<PackageItemResponse> Items { get; set; } = new();
    }

    public class PackageTierResponse
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int PriceCents { get; set; }
        public string DayScope { get; set; } = "any";
        public bool AfternoonOnly { get; set; }
        public int SessionCount { get; set; }
        public int SortOrder { get; set; }
    }

    public class PackageSlotResponse
    {
        public Guid Id { get; set; }
        public string DayScope { get; set; } = "any";
        public string StartTime { get; set; } = "09:00";
        public bool IsAfternoon { get; set; }
        public int Capacity { get; set; }
        public Guid? InstructorId { get; set; }
    }

    public class PackageItemResponse
    {
        public Guid Id { get; set; }
        public string ItemType { get; set; } = "gear";
        public Guid VariantId { get; set; }
        public int Quantity { get; set; }
        public string? Name { get; set; }
        public string? VariantLabel { get; set; }
        public int DepositCents { get; set; }
    }

    /// <summary>Availability + price for a chosen date and tier.</summary>
    public class PackageAvailabilityResponse
    {
        public bool Available { get; set; }
        public string? Reason { get; set; }
        public int PriceCents { get; set; }
        public int DepositCents { get; set; }
        /// <summary>Bookable coached session times for the date, as "HH:mm", with remaining capacity.</summary>
        public List<PackageSlotAvailability> Sessions { get; set; } = new();
    }

    public class PackageSlotAvailability
    {
        public Guid SlotId { get; set; }
        public string StartTime { get; set; } = "09:00";
        public int Remaining { get; set; }
    }

    public class PackageBookResult
    {
        public Guid PurchaseId { get; set; }
        public string Status { get; set; } = "pending";
        public string? ClientSecret { get; set; }
        public string? DepositClientSecret { get; set; }
        public int TotalCents { get; set; }
        public int DepositCents { get; set; }
    }
}
