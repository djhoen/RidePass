namespace webapi.Controllers.API.Data.Purchase
{
    /// <summary>Tenant-admin refund of a single purchase. AmountCents null = full-minus-service-charge default.</summary>
    public class RefundRequest
    {
        public string Kind { get; set; } = null!;   // pass | event_ticket | season_pass | membership | event_extra
        public Guid PurchaseId { get; set; }
        public int? AmountCents { get; set; }
        public string? Reason { get; set; }
        // Refund even if the purchase is already checked in / used. Requires sales.refund.override.
        public bool ForceCheckedIn { get; set; }
    }

    /// <summary>Refund every line on the same order (all purchases sharing the anchor's Stripe
    /// PaymentIntent). Each line is refunded in full (including the service charge).</summary>
    public class RefundOrderRequest
    {
        public string Kind { get; set; } = null!;   // kind of the anchor purchase
        public Guid PurchaseId { get; set; }        // any one purchase in the order
        public string? Reason { get; set; }
        public bool ForceCheckedIn { get; set; }    // requires sales.refund.override
    }

    /// <summary>Refund a caller-selected set of order lines, each in full (including the service
    /// charge). Backs the Admin Order details refund modal, where the user picks which items to
    /// refund.</summary>
    public class RefundLinesRequest
    {
        public List<RefundLineRef> Lines { get; set; } = new();
        public string? Reason { get; set; }
        public bool ForceCheckedIn { get; set; }    // requires sales.refund.override
    }

    public class RefundLineRef
    {
        public string Kind { get; set; } = null!;   // event_ticket | event_extra | season_pass | membership
        public Guid Id { get; set; }
    }
}
