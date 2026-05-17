using Services.Repositories.Data.CouponData;
using Services.Repositories.Interfaces;

namespace Services.Coupons
{
    public interface ICouponValidator
    {
        /// <summary>
        /// Look up a coupon by code (case-insensitive) and validate against the purchase
        /// context. Returns the coupon plus the rounded discount that applies to this
        /// subtotal, or an error string ready to surface to the rider. Does NOT record
        /// a redemption — that's the caller's job after the purchase row is created.
        /// </summary>
        Task<(CouponApplication? application, string? error)> ValidateAsync(
            Guid tenantId, string code, string scope, Guid? eventId, int subtotalCents, Guid? userId);
    }

    public class CouponValidator : ICouponValidator
    {
        private readonly ICouponRepository _coupons;

        public CouponValidator(ICouponRepository coupons) => _coupons = coupons;

        public async Task<(CouponApplication? application, string? error)> ValidateAsync(
            Guid tenantId, string code, string scope, Guid? eventId, int subtotalCents, Guid? userId)
        {
            if (string.IsNullOrWhiteSpace(code)) return (null, "Coupon code is empty.");
            var coupon = await _coupons.GetByCode(tenantId, code.Trim());
            if (coupon is null) return (null, "That coupon code isn't valid here.");
            if (!coupon.IsActive) return (null, "That coupon is no longer active.");

            var now = DateTime.UtcNow;
            if (coupon.ValidFromUtc.HasValue && now < coupon.ValidFromUtc.Value)
                return (null, "That coupon isn't valid yet.");
            if (coupon.ValidToUtc.HasValue && now > coupon.ValidToUtc.Value)
                return (null, "That coupon has expired.");

            // Scope filter: 'all' coupons apply anywhere; otherwise the scope must match.
            if (coupon.ApplicableScope != "all" && coupon.ApplicableScope != scope)
                return (null, $"That coupon doesn't apply to {ScopeLabel(scope)}.");

            // Event filter (only meaningful for event_ticket scope).
            if (coupon.ApplicableEventId.HasValue && coupon.ApplicableEventId != eventId)
                return (null, "That coupon doesn't apply to this event.");

            if (coupon.MaxTotalUses.HasValue)
            {
                var total = await _coupons.CountRedemptions(coupon.Id);
                if (total >= coupon.MaxTotalUses.Value)
                    return (null, "That coupon has been fully redeemed.");
            }

            if (coupon.MaxUsesPerUser.HasValue && userId.HasValue)
            {
                var perUser = await _coupons.CountUserRedemptions(coupon.Id, userId.Value);
                if (perUser >= coupon.MaxUsesPerUser.Value)
                    return (null, "You've already used this coupon the maximum number of times.");
            }

            // Compute discount. Cap at the subtotal so amount-off coupons don't make the
            // line go negative (and a 100% percent coupon yields exactly subtotalCents off).
            int discount = coupon.DiscountKind == "percent"
                ? (int)((long)subtotalCents * coupon.DiscountValue / 10_000L)
                : coupon.DiscountValue;
            if (discount > subtotalCents) discount = subtotalCents;
            if (discount <= 0) return (null, "That coupon has no effect on this purchase.");

            return (new CouponApplication { Coupon = coupon, DiscountCents = discount }, null);
        }

        private static string ScopeLabel(string scope) => scope switch
        {
            "pass" => "passes",
            "event_ticket" => "event tickets",
            "season_pass" => "season passes",
            _ => "this purchase",
        };
    }
}
