namespace webapi.Controllers.API.Data.Voucher
{
    public class StaffVoucherItem
    {
        public Guid UserId { get; set; }
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
        public bool IsAdmin { get; set; }
        public bool Unlimited { get; set; }
        public int? YearlyAllowance { get; set; }
        public int IssuedThisYear { get; set; }
        public int RedeemedThisYear { get; set; }
        public int? Remaining { get; set; }
    }
}
