namespace webapi.Controllers.API.Data.Counter
{
    public class CounterRiderWaiverStatusRequest
    {
        public List<Guid> PurchaseIds { get; set; } = new();
    }
}
