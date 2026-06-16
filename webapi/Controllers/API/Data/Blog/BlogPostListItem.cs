namespace webapi.Controllers.API.Data.Blog
{
    /// <summary>Admin blog list row (includes drafts).</summary>
    public class BlogPostListItem
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = null!;
        public string Slug { get; set; } = null!;
        public string Status { get; set; } = null!;       // draft | published
        public bool IsFeatured { get; set; }
        public string? MainImageUrl { get; set; }
        public string? Excerpt { get; set; }
        public int ImageCount { get; set; }
        public DateTime? PublishedAtUtc { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }
}
