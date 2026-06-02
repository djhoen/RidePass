namespace Services.Helpers.Interfaces
{
    /// <summary>
    /// Per-tenant SMS pricing surface. Today the rate is global ($0.02 per
    /// outbound segment for everyone, sourced from config). Once per-tenant
    /// pricing tiers land — promotional rates, volume discounts, free tiers
    /// for early customers — this interface gains a `(Guid tenantId)` overload
    /// without disturbing the call sites that need to estimate cost or push
    /// billing events.
    /// </summary>
    public interface ISmsPricing
    {
        /// <summary>Cost RidePass charges the tenant per outbound segment, in whole cents.</summary>
        int OutboundPerSegmentCents { get; }

        /// <summary>
        /// Estimate the cost in cents of sending the given body to the given
        /// recipient count. Segments are counted via <see cref="SmsSegmentCounter"/>
        /// so the estimate matches what we'll actually bill on send.
        /// </summary>
        int EstimateOutboundCents(string body, int recipientCount);
    }
}
