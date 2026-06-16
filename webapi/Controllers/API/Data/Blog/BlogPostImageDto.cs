namespace webapi.Controllers.API.Data.Blog
{
    /// <summary>One of a post's additional gallery images.</summary>
    public class BlogPostImageDto
    {
        public Guid Id { get; set; }
        public string ImageUrl { get; set; } = null!;
        public string? Caption { get; set; }
        public int SortOrder { get; set; }
    }
}
