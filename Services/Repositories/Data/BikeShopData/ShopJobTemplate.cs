namespace Services.Repositories.Data.BikeShopData
{
    /// <summary>A saved repair job the shop performs regularly, so its labor and parts can be
    /// dropped onto a work order instead of retyped. The shop's equivalent of a standard job in
    /// a dealer system.</summary>
    public class ShopJobTemplate
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = null!;
        /// <summary>Free-text fit note, e.g. "250F four-strokes". Deliberately not a structured
        /// vehicle model: what a job fits is a mechanic's judgment, not a lookup table.</summary>
        public string? FitsNote { get; set; }
        /// <summary>Appended to the work order's intake notes when applied, so standard caveats
        /// travel with the job.</summary>
        public string? Notes { get; set; }
        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; } = 100;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class ShopJobTemplateLine
    {
        public Guid Id { get; set; }
        public Guid TemplateId { get; set; }
        public string LineKind { get; set; } = "labor";   // labor | part
        public string? Description { get; set; }
        public Guid? VariantId { get; set; }
        public int Quantity { get; set; } = 1;
        /// <summary>Labor: the rate to charge. Parts: null means resolve the variant's CURRENT
        /// price when the template is applied, so a saved price can't go stale.</summary>
        public int? UnitPriceCents { get; set; }
        /// <summary>Standard time for this labor line; auto-fills the estimate when the job is applied.</summary>
        public int? EstimatedMinutes { get; set; }
        public int SortOrder { get; set; } = 100;
        public DateTime CreatedAt { get; set; }
    }

    public class ShopJobTemplateWithLines : ShopJobTemplate
    {
        public List<ShopJobTemplateLine> Lines { get; set; } = new();
    }
}
