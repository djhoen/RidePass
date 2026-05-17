namespace Services.Repositories.Data.PaymentData
{
    public class PassProduct
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public int PriceCents { get; set; }
        public bool IsActive { get; set; }
        public int SortOrder { get; set; }
        public bool RequiresWaiver { get; set; }
        public int RiderPaidServiceChargeBps { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
