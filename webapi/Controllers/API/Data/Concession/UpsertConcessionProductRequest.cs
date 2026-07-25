using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Concession
{
    public class UpsertConcessionProductRequest
    {
        [Required, MaxLength(200)]
        public string Name { get; set; } = null!;

        [MaxLength(1000)]
        public string? Description { get; set; }

        // Tenant-defined category (null = uncategorized). Validated server-side against this tenant's categories.
        public Guid? CategoryId { get; set; }

        [Range(0, int.MaxValue)]
        public int PriceCents { get; set; }

        public string? ImageUrl { get; set; }
        // Whether this item appears in the menu-board photo carousel (only shows when it has an image).
        public bool ShowInCarousel { get; set; } = true;
        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; }
        // Kitchen station that prepares this item (null = default queue).
        public Guid? StationId { get; set; }
        // False = grab-and-go (bagged chips, canned soda): never listed on the cook screen. Default true.
        public bool RequiresPrep { get; set; } = true;
        // Whether this entree can be upgraded via the shared "make it a combo" definition.
        public bool ComboAvailable { get; set; }
        // Tax category that sets this item's rate (null = the tenant's default category). Validated
        // server-side against this tenant's categories.
        public Guid? TaxCategoryId { get; set; }
        // Stock count for simple (no-variant) items. Null = unlimited. Variant items track stock per variant.
        [Range(0, int.MaxValue)]
        public int? Inventory { get; set; }
        // Modifier groups (by id) that apply to this item, in display order.
        public List<Guid> ModifierGroupIds { get; set; } = new();
        // Options (by id) pre-selected by default when the item is added. Validated to options of the
        // item's assigned groups.
        public List<Guid> DefaultModifierOptionIds { get; set; } = new();
    }
}
