namespace webapi.Controllers.API.Data.LessonAssessment
{
    /// <summary>Tickets (event_ticket_purchase ids) to look up self-assessments for.</summary>
    public class TicketIdsRequest
    {
        public List<Guid> TicketIds { get; set; } = new();
    }
}
