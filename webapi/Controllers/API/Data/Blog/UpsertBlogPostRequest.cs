using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Blog
{
    public class UpsertBlogPostRequest
    {
        [Required, MaxLength(200)]
        public string Title { get; set; } = null!;

        // Optional. When blank, the slug is derived from the title. Uniqueness per tenant
        // is enforced server-side (a numeric suffix is appended on collision).
        [MaxLength(200)]
        public string? Slug { get; set; }

        [MaxLength(500)]
        public string? Excerpt { get; set; }

        // Rich-text body (Tiptap HTML).
        public string? BodyHtml { get; set; }

        // Main cover image URL, uploaded via POST /Blog/Image first.
        public string? MainImageUrl { get; set; }

        [RegularExpression("^(draft|published)$")]
        public string Status { get; set; } = "draft";
    }
}
