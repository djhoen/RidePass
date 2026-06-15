namespace webapi.Controllers.API.Data.Concession
{
    // Cart the cashier rang up. Each line is a product + optional chosen variant + qty.
    public class ConcessionSaleRequest
    {
        public List<SaleLine> Items { get; set; } = new();

        public class SaleLine
        {
            public Guid ProductId { get; set; }
            public Guid? VariantId { get; set; }
            public int Quantity { get; set; }
        }
    }
}
