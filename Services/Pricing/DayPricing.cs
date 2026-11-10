using Services.Repositories.Data.PaymentData;
using Services.Repositories.Data.PricingData;

namespace Services.Pricing
{
    /// <summary>
    /// Per-day prices (Script0316, Highland go-live item 13). A ticket type has an online price
    /// (PriceCents) and optionally a walk-up price for Gate Sale; a day pass (gate_fee) on a peak day
    /// costs more by the venue's day rules. A rule for the specific date beats the weekday rules; among
    /// rules of the same kind the biggest increase wins. Rules never stack, and never apply to race
    /// entries (priced per race) or to a free ticket.
    /// </summary>
    public static class DayPricing
    {
        public const string Online = "online";
        public const string WalkUp = "walk_up";

        public readonly record struct Result(int PriceCents, int BaseCents, string? RuleLabel);

        public static Result For(EventTicketTier tier, DateOnly eventDay, Guid? eventTypeId, string channel,
            IReadOnlyList<DayPriceRule> rules)
        {
            var baseCents = channel == WalkUp && tier.WalkUpPriceCents is int walkUp ? walkUp : tier.PriceCents;
            if (tier.Kind != "gate_fee" || baseCents <= 0) return new Result(baseCents, baseCents, null);

            var matching = rules.Where(r => r.IsActive
                && (r.EventTypeId is null || r.EventTypeId == eventTypeId)
                && (r.OnDate is DateTime d ? DateOnly.FromDateTime(d) == eventDay : r.Weekday == (int)eventDay.DayOfWeek))
                .ToList();
            var dated = matching.Where(r => r.OnDate is not null).ToList();
            var pool = dated.Count > 0 ? dated : matching;

            DayPriceRule? best = null;
            var bestAdd = 0;
            foreach (var r in pool)
            {
                var add = Increase(r, baseCents);
                if (add > bestAdd) { best = r; bestAdd = add; }
            }
            return best is null
                ? new Result(baseCents, baseCents, null)
                : new Result(Math.Min(baseCents + bestAdd, 1_000_000), baseCents, best.Label);
        }

        public static int Increase(DayPriceRule r, int baseCents) =>
            r.AdjustKind == "percent" ? (int)((long)baseCents * r.AdjustValue / 10_000L) : r.AdjustValue;
    }
}
