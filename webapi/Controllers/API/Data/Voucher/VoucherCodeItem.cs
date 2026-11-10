namespace webapi.Controllers.API.Data.Voucher
{
    public class VoucherCodeItem
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = "";
        /// <summary>unused | redeemed | voided | expired</summary>
        public string Status { get; set; } = "unused";
        public DateTime? ExpiresAtUtc { get; set; }
        public DateTime? RedeemedAtUtc { get; set; }
        public Guid? RedeemedByUserId { get; set; }
        public string? RedeemedByName { get; set; }
        public string? RiderName { get; set; }
        public string? EventTitle { get; set; }
    }
}
