namespace webapi.Controllers.API.Data.Concession
{
    public class ConcessionCategoryResponse
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
    }
}
