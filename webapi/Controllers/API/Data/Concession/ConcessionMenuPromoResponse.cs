namespace webapi.Controllers.API.Data.Concession
{
    public class ConcessionMenuPromoResponse
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = null!;
        public string? Subtitle { get; set; }
        public string? ImageUrl { get; set; }
        public Guid? MenuBoardId { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
    }
}
