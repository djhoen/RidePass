namespace webapi.Controllers.API.Data.Concession
{
    public class ConcessionVariantResponse
    {
        public Guid Id { get; set; }
        public Guid ProductId { get; set; }
        public string? Size { get; set; }
        public string? Color { get; set; }
        public int? PriceCents { get; set; }   // null = use product price
        public string? ImageUrl { get; set; }
        public int? Inventory { get; set; }     // null = unlimited
        public int Sold { get; set; }
        public int Remaining { get; set; }      // -1 if unlimited
        public bool IsActive { get; set; }
        public int SortOrder { get; set; }
    }
}
