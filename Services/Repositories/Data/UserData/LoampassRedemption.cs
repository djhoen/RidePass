namespace Services.Repositories.Data.UserData
{
    /// <summary>
    /// Records that a Loam Pass credit was drawn from a specific linked LoamMx account to cover
    /// a RidePass event-ticket entry, with the idempotency key used so a refund can reverse it.
    /// </summary>
    public class LoampassRedemption
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid EventTicketPurchaseId { get; set; }
        public string LoampassAccountId { get; set; } = null!;
        public string DestinationId { get; set; } = null!;
        public string IdempotencyKey { get; set; } = null!;
        public string Status { get; set; } = "redeemed";   // redeemed | refunded
        public DateTime CreatedAt { get; set; }
        public DateTime? RefundedAt { get; set; }
    }
}
