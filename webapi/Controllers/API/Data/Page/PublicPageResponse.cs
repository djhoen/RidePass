namespace webapi.Controllers.API.Data.Page
{
    /// <summary>Public page detail (published pages only, resolved by tenant + slug).</summary>
    public class PublicPageResponse
    {
        public string Title { get; set; } = null!;
        public string Slug { get; set; } = null!;
        public string? BodyHtml { get; set; }
        public string? HeroImageUrl { get; set; }
        public DateTime? PublishedAtUtc { get; set; }
    }
}
