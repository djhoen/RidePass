namespace Services.Repositories.Data.PaymentData
{
    public class EventTicketTier
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid EventId { get; set; }
        public string Name { get; set; } = null!;
        public int PriceCents { get; set; }
        public int? Inventory { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class EventTicketTierSoldCount
    {
        public Guid TierId { get; set; }
        public int SoldCount { get; set; }
    }
}
