namespace Services.Repositories.Data.CouponData
{
    public class Coupon
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Code { get; set; } = null!;
        public string? Description { get; set; }
        public string DiscountKind { get; set; } = "percent";  // 'percent' (bps) | 'amount' (cents)
        public int DiscountValue { get; set; }                  // bps if percent, cents if amount
        public string ApplicableScope { get; set; } = "all";    // all | pass | event_ticket | season_pass
        public Guid? ApplicableEventId { get; set; }
        public DateTime? ValidFromUtc { get; set; }
        public DateTime? ValidToUtc { get; set; }
        public int? MaxTotalUses { get; set; }
        public int? MaxUsesPerUser { get; set; }
        public bool IsActive { get; set; }
        public Guid? CreatedByUserId { get; set; }
        // When set, this coupon was minted for a specific rider (Phase 2 race-entry bundle).
        // Public tenant coupons leave both fields null.
        public Guid? IssuedToUserId { get; set; }
        public Guid? IssuedFromPurchaseId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class CouponRedemption
    {
        public Guid Id { get; set; }
        public Guid CouponId { get; set; }
        public Guid TenantId { get; set; }
        public Guid? UserId { get; set; }
        public string SourceKind { get; set; } = null!;  // pass | event_ticket | season_pass
        public Guid SourceId { get; set; }
        public int DiscountCents { get; set; }
        public DateTime RedeemedAt { get; set; }
    }

    /// <summary>
    /// Result of a successful coupon validation: the coupon plus the discount that
    /// applies to a given subtotal. Use <see cref="DiscountCents"/> directly when
    /// charging — already rounded.
    /// </summary>
    public class CouponApplication
    {
        public Coupon Coupon { get; set; } = null!;
        public int DiscountCents { get; set; }
    }

    /// <summary>
    /// One row per send-to-friend action a rider takes from their My Passes view.
    /// Captures the recipient email so the tenant gets a marketing list of warm leads.
    /// </summary>
    public class CouponShare
    {
        public Guid Id { get; set; }
        public Guid CouponId { get; set; }
        public Guid TenantId { get; set; }
        public Guid? SenderUserId { get; set; }
        public string RecipientEmail { get; set; } = null!;
        public string? RecipientName { get; set; }
        public string? PersonalNote { get; set; }
        public DateTime SentAt { get; set; }
        public DateTime? RedeemedAt { get; set; }
    }
}
