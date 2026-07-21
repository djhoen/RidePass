using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.BikeShop
{
    /// <summary>One line onto a work order: labor (description + price) or a part (variant + qty;
    /// price defaults to the variant's sale price when omitted).</summary>
    public class AddShopWorkOrderLineRequest
    {
        [Required, RegularExpression("^(labor|part)$")]
        public string LineKind { get; set; } = "labor";

        [MaxLength(300)] public string? Description { get; set; }
        public Guid? VariantId { get; set; }
        [Range(1, 1000)] public int Quantity { get; set; } = 1;
        [Range(0, 10_000_000)] public int? UnitPriceCents { get; set; }

        /// <summary>Labor only. When set (and the tenant has a labor rate), the line is priced from
        /// hours * rate and stores both, rather than a typed total. Omit for a flat labor charge.</summary>
        [Range(0.01, 9999)] public decimal? LaborHours { get; set; }

        /// <summary>Labor only. Estimated time for this line, summed into the job estimate.</summary>
        [Range(0, 100000)] public int? EstimatedMinutes { get; set; }
    }
}
