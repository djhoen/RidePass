using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.SeasonPass
{
    /// <summary>
    /// Replace what an employee pass grants. Scoped deliberately: it touches ONLY the benefit
    /// rows, so the Employee Passes page cannot blank a product's landing page, slug, or pricing
    /// the way a full product upsert from a page that doesn't know those fields would.
    /// </summary>
    public class UpdateEmployeeBenefitsRequest
    {
        [Required]
        public List<SeasonPassBenefitInput> Benefits { get; set; } = new();
    }
}
