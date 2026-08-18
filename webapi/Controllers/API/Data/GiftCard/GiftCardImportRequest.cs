using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.GiftCard
{
    // Raw CSV text ("code,balance[,recipient_name[,recipient_email]]", balance in dollars, header
    // row optional). The server parses and validates; dryRun reports without writing anything.
    public class GiftCardImportRequest
    {
        [Required, MaxLength(2_000_000)]
        public string CsvText { get; set; } = null!;
        public bool DryRun { get; set; } = true;
        [MaxLength(120)]
        public string? Source { get; set; }   // e.g. "Card Dog / old POS"; defaults to "Legacy import"
    }
}
