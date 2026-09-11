namespace webapi.Controllers.API.Data.Newsletter
{
    public class AudienceItem
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsSample { get; set; }
        public AudienceDefinitionDto Definition { get; set; } = new();
        /// <summary>"Everyone who holds a current pass", for the list and the campaign picker.</summary>
        public string Summary { get; set; } = string.Empty;
        /// <summary>People in it as of the last refresh (hourly, and whenever the audience is saved). The builder counts live.</summary>
        public int MemberCount { get; set; }
        public int UsedByCampaigns { get; set; }
        public int UsedByAutomations { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
