namespace webapi.Controllers.API.Data.Concession
{
    public class ConcessionTaxCategoryResponse
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public int RateBps { get; set; }   // basis points: 825 = 8.25%
        public bool IsDefault { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
    }
}
