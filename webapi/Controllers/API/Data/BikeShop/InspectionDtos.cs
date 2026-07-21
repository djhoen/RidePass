using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.BikeShop
{
    /// <summary>
    /// Start an inspection for a bike. The work order is optional context for the visit it happened
    /// on; the bike is what the inspection actually belongs to.
    /// </summary>
    public class StartInspectionRequest
    {
        [Required] public Guid CustomerBikeId { get; set; }
        public Guid? WorkOrderId { get; set; }
        /// <summary>Which checklist to use. Omit for the shop's default.</summary>
        public Guid? TemplateId { get; set; }
        /// <summary>Defaults to +6 months when omitted.</summary>
        public DateTime? NextServiceDate { get; set; }
    }

    public class SaveInspectionRequest
    {
        /// <summary>draft while the mechanic works; complete when it's ready for the customer.</summary>
        public string Status { get; set; } = "draft";
        public DateTime? NextServiceDate { get; set; }
        [MaxLength(4000)] public string? SummaryNotes { get; set; }
        public List<SaveInspectionResultRow>? Results { get; set; }
    }

    public class SaveInspectionResultRow
    {
        [Required] public Guid Id { get; set; }
        /// <summary>good | monitor | attention | na</summary>
        [Required] public string Rating { get; set; } = "na";
        [MaxLength(1000)] public string? Notes { get; set; }
    }

    /// <summary>Create or rename a checklist. MakeDefault promotes it to the shop's starting list.</summary>
    public class UpsertInspectionTemplateRequest
    {
        [Required, MaxLength(120)] public string Name { get; set; } = null!;
        public bool IsActive { get; set; } = true;
        public bool MakeDefault { get; set; }
    }

    /// <summary>A checklist row. Omit Id to add, supply it to edit.</summary>
    public class UpsertInspectionItemRequest
    {
        public Guid? Id { get; set; }
        [Required, MaxLength(80)] public string GroupLabel { get; set; } = null!;
        [Required, MaxLength(160)] public string Label { get; set; } = null!;
        public int SortOrder { get; set; } = 100;
        public bool IsActive { get; set; } = true;
    }
}
