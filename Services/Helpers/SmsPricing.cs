using Microsoft.Extensions.Configuration;
using Services.Helpers.Interfaces;

namespace Services.Helpers
{
    public class SmsPricing : ISmsPricing
    {
        private const int DefaultPerSegmentCents = 2;

        public int OutboundPerSegmentCents { get; }

        public SmsPricing(IConfiguration config)
        {
            OutboundPerSegmentCents = int.TryParse(
                config["Sms:Pricing:OutboundPerSegmentCents"], out var v) && v > 0
                ? v
                : DefaultPerSegmentCents;
        }

        public int EstimateOutboundCents(string body, int recipientCount)
        {
            if (recipientCount <= 0) return 0;
            var segments = SmsSegmentCounter.Count(body).Segments;
            return segments * recipientCount * OutboundPerSegmentCents;
        }
    }
}
