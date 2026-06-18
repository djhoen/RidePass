namespace webapi.Controllers.API.Data.PlatformBranding
{
    /// <summary>
    /// Super-admin edit payload for the For Tracks (operator-acquisition) page.
    /// Scoped to just the For Tracks hero + the "Why Tracks love RidePass" benefits
    /// block so it never overwrites the apex home-page fields (separate save endpoint).
    /// The benefits image is uploaded separately via the "benefits" image endpoint.
    /// </summary>
    public class SaveForTracksRequest
    {
        public string? HeroEyebrow { get; set; }
        public string? HeroHeadline { get; set; }
        public string? HeroSubhead { get; set; }
        public string? BenefitsTitle { get; set; }
        public string? BenefitsHtml { get; set; }
    }
}
