namespace webapi.Controllers.API.Data.Event
{
    public class EventResponse
    {
        public Guid Id { get; set; }
        public Guid EventTypeId { get; set; }
        public string EventTypeCode { get; set; } = null!;
        public string EventTypeName { get; set; } = null!;
        public string EventTypeColor { get; set; } = null!;
        public string? EventTypeImageUrl { get; set; }
        public string Title { get; set; } = null!;
        public string? Description { get; set; }
        public DateTime StartsAtUtc { get; set; }
        public DateTime EndsAtUtc { get; set; }
        public bool AllDay { get; set; }
        public int? Capacity { get; set; }
        public string? LocationLabel { get; set; }
        public string Status { get; set; } = null!;
        public bool RequiresRiderWaiver { get; set; }
        public bool RequiresSpectatorWaiver { get; set; }
        public Guid? SpectatorWaiverId { get; set; }
        public Guid? RacerWaiverId { get; set; }
        public string? ImageUrl { get; set; }
        public bool HasActiveTiers { get; set; }
        // Per-kind flags so the rider UI can show separate "Buy Ticket" / "Buy Race Entry"
        // buttons when both are offered, or a single button when only one is.
        public bool HasSpectatorTiers { get; set; }
        public bool HasRaceEntryTiers { get; set; }
        public int? MinTicketPriceCents { get; set; }
        public int? SpotsReserved { get; set; } // null if no capacity
        // Day-pass products eligible for reservation at this event. Empty list = no
        // pass option (rider sees no Reserve-a-pass button on the event modal).
        public List<EligiblePassProduct> EligiblePasses { get; set; } = new();
        public List<EligibleExtra> EligibleExtras { get; set; } = new();
    }

    public class EligiblePassProduct
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public int PriceCents { get; set; }
        public bool RequiresWaiver { get; set; }
        public bool IsActive { get; set; }
    }

    public class EligibleExtra
    {
        public Guid ProductId { get; set; }
        public string Name { get; set; } = null!;
        public string Kind { get; set; } = null!;
        public int PriceCents { get; set; }
        public string? ImageUrl { get; set; }
        public int? Inventory { get; set; }
        public int Sold { get; set; }
        public int Remaining { get; set; }   // -1 if unlimited
        public bool RequiresWaiver { get; set; }
        // Empty for legacy single-SKU products. When populated, the rider picker
        // shows size/color/gender dropdowns instead of a flat qty +/-.
        public List<EligibleExtraVariant> Variants { get; set; } = new();
    }

    public class EligibleExtraVariant
    {
        public Guid Id { get; set; }
        public string? Size { get; set; }
        public string? Color { get; set; }
        public string? Gender { get; set; }
        // Effective price = (variant override) ?? product price.
        public int PriceCents { get; set; }
        // Effective image = (variant override) ?? product image.
        public string? ImageUrl { get; set; }
        public int? Inventory { get; set; }
        public int Sold { get; set; }
        public int Remaining { get; set; }   // -1 if unlimited
    }
}
