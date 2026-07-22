namespace webapi.Controllers.API.Data.Me
{
    public class MyPurchaseResponse
    {
        public string Kind { get; set; } = null!;
        public Guid Id { get; set; }
        public string ItemName { get; set; } = null!;
        public Guid? EventId { get; set; }
        public DateTime? EventStartsAtUtc { get; set; }
        public DateTime? ValidOnDate { get; set; }
        public int AmountCents { get; set; }
        public string Status { get; set; } = null!;
        public Guid RedemptionToken { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        // Only set when Kind == "event_ticket": 'race_entry' or 'spectator_pass'.
        // Lets the UI conditionally show race-only actions like share-registration.
        public string? TierKind { get; set; }
        // The registered rider/holder this admission is for, so a buyer holding several passes can
        // tell which QR belongs to whom. Null until the ticket is registered to a rider.
        public string? HolderName { get; set; }
    }
}
