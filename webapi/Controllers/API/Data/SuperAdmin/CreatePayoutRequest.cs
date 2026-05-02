using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.SuperAdmin
{
    public class CreatePayoutRequest
    {
        [Required]
        public DateTime PeriodStartUtc { get; set; }

        [Required]
        public DateTime PeriodEndUtc { get; set; }

        public string? Memo { get; set; }
    }
}
