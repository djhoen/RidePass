namespace webapi.Controllers.API.Data.Purchase
{
    /// <summary>Tenant-admin refund of a single purchase. AmountCents null = full-minus-service-charge default.</summary>
    public class RefundRequest
    {
        public string Kind { get; set; } = null!;   // pass | event_ticket | season_pass | membership | event_extra
        public Guid PurchaseId { get; set; }
        public int? AmountCents { get; set; }
        public string? Reason { get; set; }
    }
}
