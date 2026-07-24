namespace webapi.Controllers.API.Data.Package
{
    /// <summary>Create or update a package with its tiers, session slots, and included items.</summary>
    public class UpsertPackageRequest
    {
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
        public List<PackageTierInput> Tiers { get; set; } = new();
        public List<PackageSlotInput> Slots { get; set; } = new();
        public List<PackageItemInput> Items { get; set; } = new();
    }

    public class PackageTierInput
    {
        public string Name { get; set; } = string.Empty;
        public int PriceCents { get; set; }
        public string DayScope { get; set; } = "any";
        public bool AfternoonOnly { get; set; }
        public int SessionCount { get; set; } = 1;
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class PackageSlotInput
    {
        public string DayScope { get; set; } = "any";
        /// <summary>"HH:mm" 24-hour.</summary>
        public string StartTime { get; set; } = "09:00";
        public bool IsAfternoon { get; set; }
        public int Capacity { get; set; } = 8;
        public Guid? InstructorId { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class PackageItemInput
    {
        public string ItemType { get; set; } = "gear";
        public Guid VariantId { get; set; }
        public int Quantity { get; set; } = 1;
        public int SortOrder { get; set; }
    }
}
