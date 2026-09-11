namespace Services.Repositories.Data.NewsletterData
{
    /// <summary>
    /// How a campaign did, in the numbers a track asks about the morning after: delivered,
    /// opened, clicked, bought, and for how much. A conversion is a paid ticket or pass by a
    /// recipient within the attribution window after their send; a click conversion is one
    /// where they clicked the email first. Same caveat as every email platform: someone who
    /// would have bought anyway still counts.
    /// </summary>
    public class CampaignReportTotals
    {
        public int Delivered { get; set; }
        public int Emails { get; set; }
        public int Texts { get; set; }
        public int Skipped { get; set; }
        public int Failed { get; set; }
        /// <summary>Distinct people delivered to (one person may get an email and a text).</summary>
        public int People { get; set; }
        public int UniqueOpens { get; set; }
        public int UniqueClicks { get; set; }
        public int Conversions { get; set; }
        public int ClickConversions { get; set; }
        public long RevenueCents { get; set; }
    }

    /// <summary>One send row with what happened after it.</summary>
    public class CampaignRecipientRow
    {
        public Guid Id { get; set; }
        public string Email { get; set; } = null!;
        public string? Name { get; set; }
        public string Channel { get; set; } = "email";
        public string Status { get; set; } = null!;
        public string? Error { get; set; }
        public DateTime? SentAt { get; set; }
        public DateTime? FirstOpenAt { get; set; }
        public DateTime? FirstClickAt { get; set; }
        public DateTime? FirstBuyAt { get; set; }
        public long? RevenueCents { get; set; }
        /// <summary>Rows matching the filter before paging (window function).</summary>
        public int Total { get; set; }
    }

    /// <summary>Purchases attributed to a campaign or an automation step.</summary>
    public class MarketingConversion
    {
        public Guid Key { get; set; }
        public int Conversions { get; set; }
        public long RevenueCents { get; set; }
    }
}
