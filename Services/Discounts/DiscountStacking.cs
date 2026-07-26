namespace Services.Discounts
{
    /// <summary>
    /// Which of the discounts on a sale actually apply, given the tenant's stacking policy
    /// (Script0254). Pure arithmetic on purpose: the counters that use it are long methods full of
    /// payment and inventory concerns, and "how much came off" is the one part that must be exactly
    /// right. Shared by the bike shop register and work-order billing so the two cannot drift.
    /// </summary>
    public static class DiscountStacking
    {
        /// <summary>The amounts that survive the policy, in the same order they went in.</summary>
        public readonly record struct Result(int BenefitCents, int StaffCents, int CouponCents)
        {
            public int Total => BenefitCents + StaffCents + CouponCents;
        }

        /// <summary>
        /// With stacking allowed, every discount applies and they sum. With stacking off (the
        /// default), exactly ONE applies: the largest, so the customer still gets the best deal
        /// available without three compounding.
        ///
        /// Ties break in a deliberate order: the season-pass benefit the customer already paid
        /// for, then the discount the counter chose, then the code the customer brought. An
        /// entitlement someone bought should not lose a coin-flip to a promotion.
        /// </summary>
        public static Result Resolve(int benefitCents, int staffCents, int couponCents, bool allowStacking)
        {
            benefitCents = Math.Max(0, benefitCents);
            staffCents = Math.Max(0, staffCents);
            couponCents = Math.Max(0, couponCents);

            if (allowStacking) return new Result(benefitCents, staffCents, couponCents);

            var best = Math.Max(benefitCents, Math.Max(staffCents, couponCents));
            if (best == 0) return new Result(0, 0, 0);

            if (benefitCents == best) return new Result(benefitCents, 0, 0);
            if (staffCents == best) return new Result(0, staffCents, 0);
            return new Result(0, 0, couponCents);
        }
    }
}
