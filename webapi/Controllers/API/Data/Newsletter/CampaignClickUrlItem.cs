namespace webapi.Controllers.API.Data.Newsletter
{
    /// <summary>One link in a sent campaign and how many distinct people clicked it.</summary>
    public class CampaignClickUrlItem
    {
        public string Url { get; set; } = string.Empty;
        public int UniqueClickers { get; set; }
        public int TotalClicks { get; set; }
    }
}
