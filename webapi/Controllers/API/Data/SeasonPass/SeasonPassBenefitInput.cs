using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.SeasonPass
{
    /// <summary>
    /// One thing a season pass product grants. Doubles as the response shape — the admin editor
    /// round-trips exactly what it sends.
    /// </summary>
    public class SeasonPassBenefitInput
    {
        /// <summary>'event' | 'concession' | 'rental' | 'retail' | 'buddy_pass'.</summary>
        [Required, RegularExpression("^(event|concession|rental|retail|buddy_pass)$")]
        public string BenefitType { get; set; } = "event";

        /// <summary>tenant_event_type id for 'event'; NULL = the whole surface.</summary>
        public Guid? ScopeId { get; set; }

        [Required, RegularExpression("^(percent|amount)$")]
        public string DiscountKind { get; set; } = "percent";

        /// <summary>Basis points when percent (10000 = 100% = included), cents when amount.</summary>
        [Range(0, 10_000_000)]
        public int DiscountValue { get; set; }

        /// <summary>Uses per season; null = unlimited.</summary>
        [Range(1, 1000)]
        public int? Quantity { get; set; }

        /// <summary>Display name of what ScopeId points at (e.g. the event type). Response-only.</summary>
        public string? ScopeName { get; set; }
    }
}
