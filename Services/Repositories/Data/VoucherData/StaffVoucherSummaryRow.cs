namespace Services.Repositories.Data.VoucherData
{
    /// <summary>A staff member's voucher allowance and this year's use (management page).</summary>
    public class StaffVoucherSummaryRow
    {
        public Guid UserId { get; set; }
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string Email { get; set; } = "";
        public string Role { get; set; } = "";
        public string[] Roles { get; set; } = System.Array.Empty<string>();
        /// <summary>True when an allowance row exists; false = never granted (0, unless an Admin).</summary>
        public bool HasAllowanceRow { get; set; }
        /// <summary>NULL with a row = unlimited.</summary>
        public int? YearlyAllowance { get; set; }
        public int IssuedThisYear { get; set; }
        public int RedeemedThisYear { get; set; }
    }
}
