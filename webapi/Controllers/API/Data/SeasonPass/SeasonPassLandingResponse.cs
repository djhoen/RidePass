namespace webapi.Controllers.API.Data.SeasonPass
{
    /// <summary>
    /// One season pass product's public landing page: the authored marketing content plus the
    /// live product facts the page renders (price, kind, credits, season dates), so the page
    /// can never drift out of date with the catalog.
    /// </summary>
    public class SeasonPassLandingResponse
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public int PriceCents { get; set; }
        public DateTime ValidFromDate { get; set; }
        public DateTime ValidToDate { get; set; }
        public string Kind { get; set; } = null!;
        public int[]? ValidDaysOfWeek { get; set; }
        public int? TotalCredits { get; set; }
        public bool RequiresWaiver { get; set; }
        public int RiderPaidServiceChargeBps { get; set; }

        public string? Slug { get; set; }
        public string? HeroImageUrl { get; set; }
        /// <summary>Raw Tiptap HTML — the client renders it through RichTextView (DOMPurify).</summary>
        public string? LandingHtml { get; set; }
        /// <summary>False only reaches admins (draft preview); the public endpoint 404s drafts.</summary>
        public bool LandingPublished { get; set; }

        /// <summary>What the pass grants, resolved with display names (same shape as the products list).</summary>
        public List<SeasonPassBenefitInput> Benefits { get; set; } = new();
    }
}
