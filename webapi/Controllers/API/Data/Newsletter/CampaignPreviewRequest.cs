namespace webapi.Controllers.API.Data.Newsletter
{
    /// <summary>Render editor HTML exactly as it would be sent, for the phone/desktop preview.</summary>
    public class CampaignPreviewRequest
    {
        public string BodyHtml { get; set; } = string.Empty;
        public string? PreviewText { get; set; }
        /// <summary>When set (an automation step), merge fields are filled with that trigger's sample values.</summary>
        public string? TriggerKind { get; set; }
    }
}
