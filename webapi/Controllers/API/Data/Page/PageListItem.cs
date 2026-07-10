namespace webapi.Controllers.API.Data.Page
{
    /// <summary>Admin page list row (includes drafts).</summary>
    public class PageListItem
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = null!;
        public string Slug { get; set; } = null!;
        public string Status { get; set; } = null!;       // draft | published
        public bool ShowInNav { get; set; }
        public string? NavLabel { get; set; }
        public int SortOrder { get; set; }
        public string? HeroImageUrl { get; set; }
        public DateTime? PublishedAtUtc { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }
}
