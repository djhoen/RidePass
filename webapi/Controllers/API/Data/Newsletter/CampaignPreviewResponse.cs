namespace webapi.Controllers.API.Data.Newsletter
{
    public class CampaignPreviewResponse
    {
        /// <summary>The complete email HTML: preheader, branded header, body, branded footer.</summary>
        public string Html { get; set; } = string.Empty;
    }
}
