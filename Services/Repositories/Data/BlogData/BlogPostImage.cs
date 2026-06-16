namespace Services.Repositories.Data.BlogData
{
    /// <summary>
    /// One of a post's additional ("several other") images. The post's main image lives
    /// on BlogPost.MainImageUrl; these are the gallery, ordered by SortOrder.
    /// </summary>
    public class BlogPostImage
    {
        public Guid Id { get; set; }
        public Guid BlogPostId { get; set; }
        public Guid TenantId { get; set; }
        public string ImageUrl { get; set; } = null!;
        public string? Caption { get; set; }
        public int SortOrder { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
