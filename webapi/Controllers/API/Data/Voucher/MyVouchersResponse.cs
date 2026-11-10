namespace webapi.Controllers.API.Data.Voucher
{
    public class MyVouchersResponse
    {
        public bool Unlimited { get; set; }
        /// <summary>Null when unlimited.</summary>
        public int? Remaining { get; set; }
        public int? YearlyAllowance { get; set; }
        public int IssuedThisYear { get; set; }
        public int Year { get; set; }
        public bool CanManage { get; set; }
        public List<VoucherBatchItem> Batches { get; set; } = new();
    }
}
