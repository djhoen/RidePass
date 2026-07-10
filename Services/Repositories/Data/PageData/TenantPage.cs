namespace Services.Repositories.Data.PageData
{
    public class TenantPage
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Title { get; set; } = null!;
        // Public URL key, unique per tenant (case-insensitive). Auto-derived from the
        // title on create but editable. Reachable at {subdomain}.ridepass.io/{slug}.
        public string Slug { get; set; } = null!;
        // Rich-text body (Tiptap HTML), may include inline images.
        public string? BodyHtml { get; set; }
        public string? HeroImageUrl { get; set; }
        public string Status { get; set; } = "draft";   // draft | published
        // Whether this page shows as a top-level nav link (public site header/drawer).
        public bool ShowInNav { get; set; }
        // Label used in the nav link when ShowInNav is true; falls back to Title if null.
        public string? NavLabel { get; set; }
        public int SortOrder { get; set; }
        public DateTime? PublishedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
