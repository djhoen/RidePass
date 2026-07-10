namespace webapi.Controllers.API.Data.Page
{
    /// <summary>Full page (admin editor + public detail page).</summary>
    public class PageDetail
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = null!;
        public string Slug { get; set; } = null!;
        public string? BodyHtml { get; set; }
        public string? HeroImageUrl { get; set; }
        public string Status { get; set; } = null!;
        public bool ShowInNav { get; set; }
        public string? NavLabel { get; set; }
        public int SortOrder { get; set; }
        public DateTime? PublishedAtUtc { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }
}
