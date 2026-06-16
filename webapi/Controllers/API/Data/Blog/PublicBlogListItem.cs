namespace webapi.Controllers.API.Data.Blog
{
    /// <summary>Public blog list card (published posts only).</summary>
    public class PublicBlogListItem
    {
        public string Title { get; set; } = null!;
        public string Slug { get; set; } = null!;
        public string? Excerpt { get; set; }
        public string? MainImageUrl { get; set; }
        public DateTime? PublishedAtUtc { get; set; }
    }
}
