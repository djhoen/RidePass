namespace webapi.Controllers.API.Data.Newsletter
{
    /// <summary>Target of a non-subscriber campaign audience. Only the field the kind needs is set.</summary>
    public class CampaignAudienceConfigDto
    {
        public Guid? EventId { get; set; }
        public Guid? EventTypeId { get; set; }
        public Guid? PassProductId { get; set; }
        // Optional window on event start for the event-type audience (UTC).
        public DateTime? FromUtc { get; set; }
        public DateTime? ToUtc { get; set; }
    }
}
