using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Voucher
{
    /// <summary>An Admin mints a list of codes for an outside party ("10 free passes for Red Bull").</summary>
    public class CreateVendorVouchersRequest
    {
        [Required, MaxLength(120)] public string Label { get; set; } = "";
        [Range(1, 500)] public int Count { get; set; } = 10;
        [MaxLength(254), EmailAddress] public string? RecipientEmail { get; set; }
        [MaxLength(500)] public string? Note { get; set; }
        public DateOnly? ExpiresOn { get; set; }
        /// <summary>Optional: good only for this event.</summary>
        public Guid? EventId { get; set; }
    }
}
