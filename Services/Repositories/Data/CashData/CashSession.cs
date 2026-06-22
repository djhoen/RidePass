namespace Services.Repositories.Data.CashData
{
    // A worker's cash-handling envelope for a shift, optionally tied to an event/day.
    // Cash sales/refunds are attributed to the worker (sold_by_user_id) on the sale
    // path, so this session carries no sale FK; it is the unit a turn-in reconciles
    // against. At most one 'open' session per worker per event at a time.
    public class CashSession
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid? EventId { get; set; }
        public Guid UserId { get; set; }
        public string? DeviceId { get; set; }
        public int OpeningFloatCents { get; set; }
        public string Status { get; set; } = "open";
        public DateTime OpenedAt { get; set; }
        public DateTime? ClosedAt { get; set; }
    }
}
