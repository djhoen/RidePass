using Services.Repositories.Data.PaymentData;

namespace Services.Pricing
{
    /// <summary>
    /// Resolves the live price of an event ticket "ladder": the set of steps sharing a
    /// ladder_group. A step has fired when it's the base step (no triggers) or any of its
    /// triggers is currently met (quantity sold, or a date). The active price is the
    /// highest-priced fired step. Pure logic so checkout and the public read path share it.
    /// </summary>
    public static class PriceStepResolver
    {
        public class LadderState
        {
            public EventTicketTier Active { get; init; } = null!;
            public int GroupSold { get; init; }
            // The next step that will fire (cheapest not-yet-fired step priced above Active),
            // for "then $X" / countdown messaging. Null when Active is the top step.
            public EventTicketTier? Next { get; init; }
        }

        public static bool HasFired(EventTicketTier step, int groupSold, DateTime eventStartUtc, DateTime nowUtc)
        {
            var hasQty = step.MinSold.HasValue;
            var hasRelDate = step.EffectiveDaysBefore.HasValue;
            var hasAbsDate = step.EffectiveAtUtc.HasValue;

            if (!hasQty && !hasRelDate && !hasAbsDate) return true;  // base step, always active

            if (hasQty && groupSold >= step.MinSold!.Value) return true;
            if (hasRelDate && nowUtc >= eventStartUtc.AddDays(-step.EffectiveDaysBefore!.Value)) return true;
            if (hasAbsDate && nowUtc >= step.EffectiveAtUtc!.Value) return true;
            return false;
        }

        public static LadderState? Resolve(
            IReadOnlyList<EventTicketTier> groupSteps, int groupSold, DateTime eventStartUtc, DateTime nowUtc)
        {
            if (groupSteps == null || groupSteps.Count == 0) return null;

            EventTicketTier? active = null;
            foreach (var s in groupSteps)
            {
                if (HasFired(s, groupSold, eventStartUtc, nowUtc) &&
                    (active is null || s.PriceCents > active.PriceCents))
                {
                    active = s;
                }
            }
            if (active is null) return null;

            EventTicketTier? next = null;
            foreach (var s in groupSteps)
            {
                if (s.PriceCents > active.PriceCents &&
                    !HasFired(s, groupSold, eventStartUtc, nowUtc) &&
                    (next is null || s.PriceCents < next.PriceCents))
                {
                    next = s;
                }
            }

            return new LadderState { Active = active, GroupSold = groupSold, Next = next };
        }
    }
}
