using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Voucher
{
    /// <summary>A staff member gives voucher codes from their allowance.</summary>
    public class IssueVouchersRequest
    {
        [Range(1, 25)] public int Count { get; set; } = 1;
        /// <summary>Who they are for (shown on the management page and in the email).</summary>
        [Required, MaxLength(120)] public string RecipientName { get; set; } = "";
        /// <summary>Optional: email the codes to them.</summary>
        [MaxLength(254), EmailAddress] public string? RecipientEmail { get; set; }
        [MaxLength(500)] public string? Note { get; set; }
        /// <summary>Last day the codes work (venue time). Null = no end date.</summary>
        public DateOnly? ExpiresOn { get; set; }
    }
}
