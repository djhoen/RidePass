namespace Services.Repositories.Data.VoucherData
{
    public class StaffVoucherAllowance
    {
        public Guid TenantId { get; set; }
        public Guid UserId { get; set; }
        /// <summary>NULL = unlimited.</summary>
        public int? YearlyAllowance { get; set; }
    }
}
