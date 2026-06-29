namespace webapi.Controllers.API.Data.Concession
{
    // Add received stock (delivery) to an inventory item's on-hand.
    public class ConcessionReceiveStockRequest
    {
        public decimal Quantity { get; set; }
    }
}
