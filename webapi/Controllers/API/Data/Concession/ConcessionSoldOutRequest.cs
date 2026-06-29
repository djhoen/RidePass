namespace webapi.Controllers.API.Data.Concession
{
    // Body for the quick "86 / sold out" toggle: true marks the item sold out for today, false clears it.
    public class ConcessionSoldOutRequest
    {
        public bool SoldOut { get; set; }
    }
}
