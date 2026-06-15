namespace webapi.Controllers.API.Data.Concession
{
    public class ConcessionProductResponse
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public string Category { get; set; } = "other";
        public int PriceCents { get; set; }
        public string? ImageUrl { get; set; }
        public bool IsActive { get; set; }
        public int SortOrder { get; set; }
        public List<ConcessionVariantResponse> Variants { get; set; } = new();
    }
}
