namespace Services.Helpers
{
    /// <summary>
    /// Per-email tiered pricing for campaign sends, charged by cumulative monthly volume.
    /// MUST stay in sync with vueapp/src/helpers/EmailPricing.ts (the in-app estimate uses
    /// the TS copy; this server copy is the source of truth for actual billing).
    /// </summary>
    public static class EmailPricing
    {
        // (upTo, cents per email). Rates drop as monthly volume climbs. Calibrated to sit just
        // under Mailchimp Essentials 2026 contact-tier anchors for a list emailed ~once a month
        // ($26.50 @ 1k, $45 @ 2.5k, $75 @ 5k, $110 @ 10k): cumulative cost is $25 @ 1k,
        // $44.50 @ 2.5k, $74.50 @ 5k, $109.50 @ 10k.
        private static readonly (long UpTo, double CentsPerEmail)[] Tiers =
        {
            (1_000, 2.5),
            (2_500, 1.3),
            (5_000, 1.2),
            (10_000, 0.7),
            (50_000, 0.5),
            (long.MaxValue, 0.3),
        };

        // Cost in (fractional) cents for `count` emails, walking the tiers cumulatively.
        public static double EstimateCents(long count)
        {
            if (count <= 0) return 0;
            double cents = 0;
            long remaining = count, floor = 0;
            foreach (var (upTo, rate) in Tiers)
            {
                if (remaining <= 0) break;
                var slice = Math.Min(remaining, upTo - floor);
                cents += slice * rate;
                remaining -= slice;
                floor = upTo;
            }
            return cents;
        }

        // Whole-cent charge for sending `count` emails when `monthToDate` were already sent
        // this month — the marginal cost across the tiers. Rounded once, at the end.
        public static int MarginalChargeCents(long monthToDate, long count)
        {
            if (count <= 0) return 0;
            var marginal = EstimateCents(monthToDate + count) - EstimateCents(monthToDate);
            return (int)Math.Round(marginal, MidpointRounding.AwayFromZero);
        }
    }
}
