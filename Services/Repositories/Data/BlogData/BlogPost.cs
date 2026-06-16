namespace Services.Repositories.Data.BlogData
{
    public class BlogPost
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Title { get; set; } = null!;
        // Public URL key, unique per tenant (case-insensitive). Auto-derived from the
        // title on create but editable.
        public string Slug { get; set; } = null!;
        // Short summary shown on the blog list cards and the home-page feature block.
        public string? Excerpt { get; set; }
        // Rich-text body (Tiptap HTML).
        public string? BodyHtml { get; set; }
        public string? MainImageUrl { get; set; }
        public string Status { get; set; } = "draft";   // draft | published
        // At most one featured post per tenant (enforced by a partial unique index).
        // Drives the full-width feature block on the tenant's public home page.
        public bool IsFeatured { get; set; }
        public DateTime? PublishedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
