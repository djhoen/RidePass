using Services.Helpers;
using Services.Pricing;
using Services.Repositories.Data.EventData;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Data.PricingData;
using Services.Repositories.Interfaces;
using webapi.Multitenancy;

namespace webapi.Helpers
{
    /// <summary>
    /// Sets a tier's price for the channel and the event's day (Script0316): the online price, or the
    /// walk-up price at Gate Sale, raised on the venue's peak days. Every path that charges or shows a
    /// ticket price calls this on the tier it loaded, before any discount, coupon, pass benefit or party
    /// math, so they all start from the same number. The rules are read once per request.
    /// </summary>
    public interface ITierDayPricer
    {
        /// <summary>Overwrites <paramref name="tier"/>.PriceCents with the day's price and returns the breakdown.</summary>
        Task<DayPricing.Result> Apply(EventTicketTier tier, Event ev, string channel);
    }

    public class TierDayPricer : ITierDayPricer
    {
        private readonly IDayPriceRuleRepository _rules;
        private readonly ITenantContext _tenantContext;
        private List<DayPriceRule>? _loaded;
        // A tier object is priced once: the same instance can back two cart lines (a ladder's active
        // step), and pricing it twice would add the peak-day increase twice.
        private readonly Dictionary<EventTicketTier, DayPricing.Result> _applied = new(ReferenceEqualityComparer.Instance);

        public TierDayPricer(IDayPriceRuleRepository rules, ITenantContext tenantContext)
        {
            _rules = rules;
            _tenantContext = tenantContext;
        }

        public async Task<DayPricing.Result> Apply(EventTicketTier tier, Event ev, string channel)
        {
            if (_applied.TryGetValue(tier, out var done)) return done;
            _loaded ??= await _rules.List(_tenantContext.TenantId, activeOnly: true);
            var day = TenantClock.LocalDate(_tenantContext.Tenant.Timezone, DateTime.SpecifyKind(ev.StartsAt, DateTimeKind.Utc));
            var result = DayPricing.For(tier, day, ev.EventTypeId, channel, _loaded);
            tier.PriceCents = result.PriceCents;
            _applied[tier] = result;
            return result;
        }
    }
}
