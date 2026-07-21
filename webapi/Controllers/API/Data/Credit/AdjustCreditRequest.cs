using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Credit
{
    public class AdjustCreditRequest
    {
        // Positive = grant, negative = correction. The balance floor rejects over-draws.
        [Range(-5_000_000, 5_000_000)] public int DeltaCents { get; set; }
        [MaxLength(300)] public string? Note { get; set; }
    }
}
