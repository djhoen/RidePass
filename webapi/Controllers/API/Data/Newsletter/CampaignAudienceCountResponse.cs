namespace webapi.Controllers.API.Data.Newsletter
{
    /// <summary>Live size of an audience as the editor is being filled in, before suppression.</summary>
    public class CampaignAudienceCountResponse
    {
        public string Kind { get; set; } = null!;
        public string Label { get; set; } = null!;
        // Distinct addresses in the audience.
        public int Recipients { get; set; }
        // Of those, how many are currently on the suppression / marketing opt-out list and will be skipped.
        public int Suppressed { get; set; }
    }
}
