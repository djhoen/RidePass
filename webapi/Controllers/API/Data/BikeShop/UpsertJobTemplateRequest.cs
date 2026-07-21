using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.BikeShop
{
    /// <summary>A saved standard job. Lines mirror a work order's shape: 'labor' carries a
    /// description, 'part' points at a stock variant.</summary>
    public class UpsertJobTemplateRequest
    {
        /// <summary>Null creates; set updates.</summary>
        public Guid? Id { get; set; }

        [Required, MaxLength(120)] public string Name { get; set; } = null!;
        [MaxLength(160)] public string? FitsNote { get; set; }
        [MaxLength(2000)] public string? Notes { get; set; }
        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; } = 100;

        public List<JobTemplateLineInput> Lines { get; set; } = new();
    }

    public class JobTemplateLineInput
    {
        [Required, RegularExpression("^(labor|part)$")]
        public string LineKind { get; set; } = "labor";
        [MaxLength(300)] public string? Description { get; set; }
        public Guid? VariantId { get; set; }
        [Range(1, 999)] public int Quantity { get; set; } = 1;
        /// <summary>Labor: the rate. Parts: null resolves the variant's current price at apply time.</summary>
        [Range(0, 1_000_000)] public int? UnitPriceCents { get; set; }
        /// <summary>Labor: standard time (minutes); auto-fills the estimate when the job is applied.</summary>
        [Range(0, 100_000)] public int? EstimatedMinutes { get; set; }
    }
}
