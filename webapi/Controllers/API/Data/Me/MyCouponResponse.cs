namespace webapi.Controllers.API.Data.Me
{
    public class MyCouponResponse
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = null!;
        public string? Description { get; set; }
        public string DiscountKind { get; set; } = "percent";  // percent | amount
        public int DiscountValue { get; set; }                  // bps if percent, cents if amount
        public string ApplicableScope { get; set; } = "all";
        public DateTime? ValidToUtc { get; set; }
        public Guid? IssuedFromPurchaseId { get; set; }
        public bool IsActive { get; set; }
        public int RedeemedCount { get; set; }
        public int? MaxTotalUses { get; set; }
        // Send-to-friend metadata
        public int ShareCount { get; set; }
        public DateTime? LastSharedAtUtc { get; set; }
        public string? LastSharedToEmail { get; set; }
    }
}
