using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Voucher
{
    /// <summary>A staff member's yearly voucher allowance. Unlimited ignores the number.</summary>
    public class SetVoucherAllowanceRequest
    {
        [Range(0, 1000)] public int? YearlyAllowance { get; set; }
        public bool Unlimited { get; set; }
    }
}
