namespace Services.Repositories.Data.EventData
{
    public class Blackout
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public DateTime StartsAt { get; set; }
        public DateTime EndsAt { get; set; }
        public bool AllDay { get; set; }
        public string? Reason { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
