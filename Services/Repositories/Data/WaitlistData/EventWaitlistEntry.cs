namespace Services.Repositories.Data.WaitlistData
{
    public class EventWaitlistEntry
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid EventId { get; set; }
        public Guid? TierId { get; set; }
        public Guid UserId { get; set; }
        public int Position { get; set; }
        public int Quantity { get; set; } = 1;
        public string? Notes { get; set; }

        public bool IsPrepaid { get; set; }
        public string? PrepayPiId { get; set; }
        public int PrepayAmountCents { get; set; }
        public string? PrepayRefundId { get; set; }
        public DateTime? PrepayRefundedAtUtc { get; set; }

        public DateTime? PromotedAtUtc { get; set; }
        public DateTime? ConfirmDeadlineUtc { get; set; }
        public Guid? ConfirmToken { get; set; }
        public Guid? CreatedPurchaseId { get; set; }
        public string? CreatedPurchaseKind { get; set; }

        // waiting | promoted | confirmed | expired | cancelled
        public string Status { get; set; } = "waiting";
        public string? CancelledReason { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
