using Services.Repositories.Data.CouponData;

namespace Services.Repositories.Interfaces
{
    /// <summary>
    /// Tenant-managed promo codes that riders apply at checkout. Distinct from
    /// reward vouchers (RewardRedemption) — coupons are typed in, scope-filtered,
    /// and have reuse limits across users + per-user.
    /// </summary>
    public interface ICouponRepository
    {
        // Admin
        Task<List<Coupon>> ListByTenant(Guid tenantId);
        Task<Coupon?> GetById(Guid id, Guid tenantId);
        Task<Coupon?> GetByCode(Guid tenantId, string code);
        Task<Guid> Create(Coupon coupon);
        Task Update(Coupon coupon);
        Task Delete(Guid id, Guid tenantId);

        // Redemption
        Task<int> CountRedemptions(Guid couponId);
        Task<int> CountUserRedemptions(Guid couponId, Guid userId);
        Task<Guid> RecordRedemption(CouponRedemption redemption);

        /// <summary>Remove redemption rows for the given sources (so a failed/abandoned checkout
        /// stops counting against the coupon's usage limit). Idempotent: re-deleting is a no-op.</summary>
        Task DeleteRedemptionsBySource(string sourceKind, IReadOnlyList<Guid> sourceIds);

        // Rider-issued coupon batches (Phase 2)
        Task<List<Coupon>> ListIssuedToUser(Guid userId, Guid tenantId);
        Task<List<Coupon>> ListIssuedFromPurchase(Guid purchaseId);

        // Send-to-friend (Phase 3)
        Task<Guid> RecordShare(CouponShare share);
        Task<List<CouponShare>> ListSharesByCoupon(Guid couponId);
        Task<List<CouponShare>> ListSharesByTenant(Guid tenantId, int take = 1000);
    }
}
