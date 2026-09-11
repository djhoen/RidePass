namespace webapi.Controllers.API.Data.Newsletter
{
    /// <summary>One filter in an audience. See Services.Repositories.Data.NewsletterData.AudienceRuleKinds.</summary>
    public class AudienceRuleDto
    {
        public string Kind { get; set; } = "event_purchased";
        /// <summary>"is not": exclude instead of include.</summary>
        public bool Negate { get; set; }
        public List<Guid> Ids { get; set; } = new();
        public List<string> Values { get; set; } = new();
        public int? Days { get; set; }
        public bool ActiveOnly { get; set; }
        public DateTime? FromUtc { get; set; }
        public DateTime? ToUtc { get; set; }
        /// <summary>Read-only sentence for the list ("bought a ticket to Spring Camp").</summary>
        public string? Label { get; set; }
    }
}
