namespace webapi.Controllers.API.Data.Voucher
{
    public class IssueVouchersResponse
    {
        public VoucherBatchItem Batch { get; set; } = new();
        public List<string> Codes { get; set; } = new();
        /// <summary>True when the codes were handed to the mailer for RecipientEmail.</summary>
        public bool Emailed { get; set; }
    }
}
