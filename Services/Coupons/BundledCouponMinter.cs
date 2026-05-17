using Services.Repositories.Data.CouponData;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Interfaces;

namespace Services.Coupons
{
    public interface IBundledCouponMinter
    {
        /// <summary>
        /// Generate the bundled coupons (if any) configured on the tier and tie them to
        /// the buyer + source purchase. Idempotent: looks up existing rows for the same
        /// purchase first and short-circuits if they already exist (so duplicate webhook
        /// deliveries don't double-mint).
        /// </summary>
        Task<List<Coupon>> MintForPurchase(EventTicketTier tier, Guid tenantId, Guid purchaseId, Guid? buyerUserId);
    }

    public class BundledCouponMinter : IBundledCouponMinter
    {
        private readonly ICouponRepository _coupons;

        public BundledCouponMinter(ICouponRepository coupons) => _coupons = coupons;

        public async Task<List<Coupon>> MintForPurchase(EventTicketTier tier, Guid tenantId, Guid purchaseId, Guid? buyerUserId)
        {
            if (tier.BundledCouponCount is null or <= 0) return new List<Coupon>();
            if (string.IsNullOrEmpty(tier.BundledCouponDiscountKind)
                || tier.BundledCouponDiscountValue is null
                || string.IsNullOrEmpty(tier.BundledCouponScope))
            {
                // Tier was misconfigured — log via repository semantics by returning empty,
                // upstream caller will treat this as a no-op.
                return new List<Coupon>();
            }

            // Idempotency: if this purchase already has coupons issued, return them as-is.
            var existing = await _coupons.ListIssuedFromPurchase(purchaseId);
            if (existing.Count > 0) return existing;

            DateTime? validTo = tier.BundledCouponExpiresInDays.HasValue
                ? DateTime.UtcNow.AddDays(tier.BundledCouponExpiresInDays.Value)
                : null;

            var minted = new List<Coupon>(tier.BundledCouponCount.Value);
            for (int i = 0; i < tier.BundledCouponCount.Value; i++)
            {
                var code = await GenerateUniqueCode(tenantId);
                var coupon = new Coupon
                {
                    TenantId = tenantId,
                    Code = code,
                    Description = $"From {tier.Name}",
                    DiscountKind = tier.BundledCouponDiscountKind,
                    DiscountValue = tier.BundledCouponDiscountValue.Value,
                    ApplicableScope = tier.BundledCouponScope,
                    // Pin bundled coupons to the same race event — perk is "bring friends
                    // to MY race", not "any future event". Validator's event check will
                    // reject attempts to use the code on a different event.
                    ApplicableEventId = tier.EventId,
                    ValidFromUtc = DateTime.UtcNow,
                    ValidToUtc = validTo,
                    MaxTotalUses = 1,           // single-use sharing codes
                    MaxUsesPerUser = 1,
                    IsActive = true,
                    CreatedByUserId = null,
                    IssuedToUserId = buyerUserId,
                    IssuedFromPurchaseId = purchaseId,
                };
                coupon.Id = await _coupons.Create(coupon);
                minted.Add(coupon);
            }
            return minted;
        }

        // Retry generator on unique-constraint clashes. After 5 attempts, give up so we
        // don't loop forever if alphabet/length are misconfigured.
        private async Task<string> GenerateUniqueCode(Guid tenantId)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                var code = CouponCodeGenerator.Generate();
                var existing = await _coupons.GetByCode(tenantId, code);
                if (existing is null) return code;
            }
            // Fall back to a longer code by appending a timestamp tail — the alphabet
            // collision space (30^8) makes this branch effectively unreachable.
            var ticks = DateTime.UtcNow.Ticks.ToString();
            return CouponCodeGenerator.Generate() + ticks.Substring(ticks.Length - 4);
        }
    }
}
