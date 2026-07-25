namespace webapi.Controllers.API.Data.Concession
{
    public class ConcessionProductResponse
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        // Tenant-defined category (null = uncategorized). Name/SortOrder drive grouping + section order.
        public Guid? CategoryId { get; set; }
        public string? CategoryName { get; set; }
        public int CategorySortOrder { get; set; }
        public int PriceCents { get; set; }
        public string? ImageUrl { get; set; }
        public bool ShowInCarousel { get; set; }
        public bool IsActive { get; set; }
        public int SortOrder { get; set; }
        public Guid? StationId { get; set; }
        // False = grab-and-go: the cook screen never lists this item (nothing to make).
        public bool RequiresPrep { get; set; } = true;
        // Tax category that sets this item's rate (null = the tenant's default category).
        public Guid? TaxCategoryId { get; set; }
        // Product-level stock for simple (no-variant) items. Inventory null = unlimited; Remaining -1 = not
        // tracked at the product level (item has variants, or unlimited).
        public int? Inventory { get; set; }
        public int Remaining { get; set; } = -1;
        // SoldOut = unavailable right now (86'd today, product stock depleted, or every variant out).
        // ManuallySoldOut = explicitly 86'd for today (lets the UI show the toggle state vs. just depleted).
        public bool SoldOut { get; set; }
        public bool ManuallySoldOut { get; set; }
        public List<ConcessionVariantResponse> Variants { get; set; } = new();
        // Modifier groups (with options) that apply to this item, so the POS can prompt for them.
        public List<ConcessionModifierGroupResponse> ModifierGroups { get; set; } = new();
        // Option ids pre-selected by default when this item is added (e.g. lettuce, tomato on a burger).
        public List<Guid> DefaultModifierOptionIds { get; set; } = new();
        // When true this entree can be upgraded via the shared "make it a combo" definition (tiers + slots).
        public bool ComboAvailable { get; set; }
    }
}
