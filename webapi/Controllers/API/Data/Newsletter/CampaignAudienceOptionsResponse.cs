namespace webapi.Controllers.API.Data.Newsletter
{
    /// <summary>Everything the campaign editor can target: recent events, event types, pass products.</summary>
    public class CampaignAudienceOptionsResponse
    {
        public List<CampaignAudienceEventOptionDto> Events { get; set; } = new();
        public List<CampaignAudienceNamedOptionDto> EventTypes { get; set; } = new();
        public List<CampaignAudienceNamedOptionDto> PassProducts { get; set; } = new();
    }

    public class CampaignAudienceEventOptionDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = null!;
        public DateTime StartsAtUtc { get; set; }
        public string Status { get; set; } = null!;
        public string EventTypeName { get; set; } = null!;
    }

    public class CampaignAudienceNamedOptionDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public bool IsActive { get; set; }
    }
}
