namespace webapi.Controllers.API.Data.Concession
{
    public class ConcessionRecipeLineResponse
    {
        public Guid InventoryItemId { get; set; }
        public string ItemName { get; set; } = null!;
        public string Unit { get; set; } = "each";
        public decimal Quantity { get; set; }
    }
}
