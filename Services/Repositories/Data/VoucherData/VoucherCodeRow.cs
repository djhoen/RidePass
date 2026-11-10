namespace Services.Repositories.Data.VoucherData
{
    /// <summary>One voucher code and, once used, who it admitted.</summary>
    public class VoucherCodeRow
    {
        public Guid Id { get; set; }
        public Guid BatchId { get; set; }
        public string Code { get; set; } = "";
        public DateTime? ValidToUtc { get; set; }
        public DateTime? VoidedAt { get; set; }
        public DateTime? RedeemedAt { get; set; }
        public Guid? RedeemedByUserId { get; set; }
        public string? RedeemedByName { get; set; }
        /// <summary>The ticket the voucher paid for, and the rider it names when not the buyer.</summary>
        public Guid? TicketPurchaseId { get; set; }
        public string? RiderName { get; set; }
        public string? EventTitle { get; set; }
    }
}
