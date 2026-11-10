namespace webapi.Controllers.API.Data.Voucher
{
    public class VoucherBatchItem
    {
        public Guid Id { get; set; }
        public string Kind { get; set; } = "";
        public Guid IssuedByUserId { get; set; }
        public string IssuedByName { get; set; } = "";
        public string? Label { get; set; }
        public string? RecipientEmail { get; set; }
        public string? Note { get; set; }
        public int CodeCount { get; set; }
        public int RedeemedCount { get; set; }
        public int VoidedCount { get; set; }
        public Guid? EventId { get; set; }
        public string? EventTitle { get; set; }
        public DateTime? ExpiresAtUtc { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }
}
