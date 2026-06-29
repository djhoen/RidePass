namespace webapi.Controllers.API.Data.Concession
{
    // Set a product's recipe (ingredients it consumes). Lines with quantity <= 0 are dropped server-side.
    public class ConcessionRecipeRequest
    {
        public List<Line> Lines { get; set; } = new();

        public class Line
        {
            public Guid InventoryItemId { get; set; }
            public decimal Quantity { get; set; }
        }
    }
}
