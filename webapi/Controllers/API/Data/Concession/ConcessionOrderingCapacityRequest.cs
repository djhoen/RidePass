using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Concession
{
    // Admin-editable online-order throttle config. The manual pause is toggled via a separate endpoint.
    public class ConcessionOrderingCapacityRequest
    {
        public bool CapacityEnabled { get; set; }
        [Range(0, 240)] public int BasePrepMinutes { get; set; } = 10;
        [Range(0, 1000)] public int MaxActiveOrders { get; set; }   // 0 = no cap
        public bool ShowQuoteTimes { get; set; } = true;
    }
}
