namespace webapi.Controllers.API.Data.Blog
{
    /// <summary>Full post (admin editor + public detail page), including gallery images.</summary>
    public class BlogPostDetail
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = null!;
        public string Slug { get; set; } = null!;
        public string? Excerpt { get; set; }
        public string? BodyHtml { get; set; }
        public string? MainImageUrl { get; set; }
        public string Status { get; set; } = null!;
        public bool IsFeatured { get; set; }
        public DateTime? PublishedAtUtc { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public List<BlogPostImageDto> Images { get; set; } = new();
    }
}
