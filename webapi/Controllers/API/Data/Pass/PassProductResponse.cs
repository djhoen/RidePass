namespace webapi.Controllers.API.Data.Pass
{
    public class PassProductResponse
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public int PriceCents { get; set; }
        public bool IsActive { get; set; }
        public int SortOrder { get; set; }
        public bool RequiresWaiver { get; set; }
        public int RiderPaidServiceChargeBps { get; set; }
    }
}
