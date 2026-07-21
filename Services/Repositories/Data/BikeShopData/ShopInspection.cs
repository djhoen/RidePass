namespace Services.Repositories.Data.BikeShopData
{
    /// <summary>The checklist definition: what this shop checks on a bike.</summary>
    public class ShopInspectionTemplate
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = null!;
        public bool IsDefault { get; set; }
        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; } = 100;
    }

    public class ShopInspectionTemplateItem
    {
        public Guid Id { get; set; }
        public Guid TemplateId { get; set; }
        public string GroupLabel { get; set; } = null!;
        public string Label { get; set; } = null!;
        public int SortOrder { get; set; } = 100;
        public bool IsActive { get; set; } = true;
    }

    /// <summary>
    /// A performed inspection. Anchored to the BIKE so grading history accrues per machine across
    /// visits; the work order is optional context for the visit it happened on.
    /// </summary>
    public class ShopInspection
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid CustomerBikeId { get; set; }
        public Guid? WorkOrderId { get; set; }
        public Guid? TemplateId { get; set; }
        public Guid? PerformedByUserId { get; set; }
        /// <summary>draft = mechanic still working; complete = ready to show the customer.</summary>
        public string Status { get; set; } = "draft";
        public DateTime PerformedAt { get; set; }
        public DateTime? NextServiceDate { get; set; }
        public string? SummaryNotes { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        /// <summary>
        /// Items needing work now, and items wearing but serviceable. Settable (not computed off
        /// Results) so a list query can populate them in SQL without loading every graded row —
        /// the history panel needs the headline numbers, not the whole checklist.
        /// </summary>
        public int AttentionCount { get; set; }
        public int MonitorCount { get; set; }
    }

    /// <summary>
    /// One graded line. Group and label are snapshotted off the template so editing the checklist
    /// later can never rewrite what a past inspection recorded.
    /// </summary>
    public class ShopInspectionResult
    {
        public Guid Id { get; set; }
        public Guid InspectionId { get; set; }
        public Guid? TemplateItemId { get; set; }
        public string GroupLabel { get; set; } = null!;
        public string Label { get; set; } = null!;
        /// <summary>good | monitor | attention | na</summary>
        public string Rating { get; set; } = "na";
        public string? Notes { get; set; }
        public int SortOrder { get; set; } = 100;
    }

    public class ShopInspectionWithResults : ShopInspection
    {
        public List<ShopInspectionResult> Results { get; set; } = new();

        /// <summary>Recomputes the headline counts from the loaded results.</summary>
        public void RecountFromResults()
        {
            AttentionCount = Results.Count(r => r.Rating == "attention");
            MonitorCount = Results.Count(r => r.Rating == "monitor");
        }
    }
}
