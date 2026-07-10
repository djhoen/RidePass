using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Page
{
    public class UpsertPageRequest
    {
        [Required, MaxLength(200)]
        public string Title { get; set; } = null!;

        // Optional. When blank, the slug is derived from the title. Uniqueness per tenant
        // is enforced server-side (a numeric suffix is appended on collision). Reserved
        // slugs that would collide with a real route are rejected.
        [MaxLength(200)]
        public string? Slug { get; set; }

        // Rich-text body (Tiptap HTML), may include inline images.
        public string? BodyHtml { get; set; }

        // Hero image URL, uploaded via POST /Page/Image first.
        public string? HeroImageUrl { get; set; }

        [RegularExpression("^(draft|published)$")]
        public string Status { get; set; } = "draft";

        public bool ShowInNav { get; set; }

        [MaxLength(100)]
        public string? NavLabel { get; set; }

        public int SortOrder { get; set; }
    }
}
