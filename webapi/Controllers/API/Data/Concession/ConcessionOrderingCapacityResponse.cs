namespace webapi.Controllers.API.Data.Concession
{
    public class ConcessionOrderingCapacityResponse
    {
        public bool CapacityEnabled { get; set; }
        public int BasePrepMinutes { get; set; }
        public int MaxActiveOrders { get; set; }
        public bool ShowQuoteTimes { get; set; }
        public bool OnlinePaused { get; set; }
    }
}
