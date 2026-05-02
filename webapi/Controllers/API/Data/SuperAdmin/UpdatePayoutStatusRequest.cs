using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.SuperAdmin
{
    public class UpdatePayoutStatusRequest
    {
        [Required]
        public string Status { get; set; } = null!;   // pending | processing | paid | failed | on_hold

        public DateTime? PayoutDateUtc { get; set; }
        public string? ExternalReference { get; set; }
        public string? Memo { get; set; }
    }
}
