namespace webapi.Controllers.API.Data.Rental
{
    public class RentalProductResponse
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public string? ImageUrl { get; set; }
        public int DailyRateCents { get; set; }
        public int DepositCents { get; set; }
        public string TrackingKind { get; set; } = null!;
        public int? InventoryPool { get; set; }
        public bool RequiresWaiver { get; set; }
        public int RiderPaidServiceChargeBps { get; set; }
        public bool IsActive { get; set; }
        public int SortOrder { get; set; }
        // For per_item products: total / available counts so the UI can show
        // "3 of 5 available" without a follow-up request per row.
        public int? PerItemTotal { get; set; }
        public int? PerItemAvailable { get; set; }
    }
}
