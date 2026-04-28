namespace webapi.Controllers.API.Data.EventTicketTier
{
    public class EventTicketTierResponse
    {
        public Guid Id { get; set; }
        public Guid EventId { get; set; }
        public string Name { get; set; } = null!;
        public int PriceCents { get; set; }
        public int? Inventory { get; set; }
        public int? Sold { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
    }
}
