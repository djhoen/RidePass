namespace webapi.Controllers.API.Data.Newsletter
{
    /// <summary>The headline numbers for one campaign, plus the links people clicked.</summary>
    public class CampaignReportResponse
    {
        /// <summary>Days after the send during which a purchase counts as a conversion.</summary>
        public int WindowDays { get; set; }
        public int Delivered { get; set; }
        public int Emails { get; set; }
        public int Texts { get; set; }
        public int Skipped { get; set; }
        public int Failed { get; set; }
        /// <summary>Distinct people delivered to.</summary>
        public int People { get; set; }
        public int UniqueOpens { get; set; }
        public int UniqueClicks { get; set; }
        /// <summary>People who bought a ticket or a pass within the window after their send.</summary>
        public int Conversions { get; set; }
        /// <summary>Of those, people who clicked the email before buying.</summary>
        public int ClickConversions { get; set; }
        public long RevenueCents { get; set; }
        public List<CampaignClickUrlItem> ClickUrls { get; set; } = new();
    }
}
