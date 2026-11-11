using Services.Repositories.Data.SurveyData;
using Services.Surveys;

namespace Services.Repositories.Interfaces
{
    /// <summary>
    /// Lesson self-assessments (Script0318): the survey an event type asks at checkout, and the answers
    /// stored per ticket in survey_response (event_ticket_purchase_id) / survey_answer. Every query is
    /// scoped through survey.tenant_id and event_ticket_purchase.tenant_id.
    /// </summary>
    public interface ILessonAssessmentRepository
    {
        /// <summary>The survey the event's type asks, unless it is closed. Null when the type asks none.</summary>
        Task<Survey?> GetSurveyForEvent(Guid eventId, Guid tenantId);

        /// <summary>The tenant's surveys that can be picked (draft or published), by name.</summary>
        Task<List<Survey>> ListPickableSurveys(Guid tenantId);

        /// <summary>
        /// Stores a ticket's answers, replacing any earlier ones for that ticket, in one transaction. Writes
        /// nothing when the ticket or the survey is not this tenant's.
        /// </summary>
        Task<Guid?> SaveForTicket(Guid tenantId, Guid surveyId, Guid ticketId, Guid? userId,
            string? respondentName, string? respondentEmail, string? ipAddress, IReadOnlyList<SurveyAnswerRow> rows);

        /// <summary>Which of these tickets (this tenant's) have a self-assessment on file.</summary>
        Task<List<Guid>> TicketsWithAssessment(Guid tenantId, IReadOnlyCollection<Guid> ticketIds);

        /// <summary>The answers on these tickets (this tenant's), flattened; see TicketAssessmentAnswer.</summary>
        Task<List<TicketAssessmentAnswer>> ListAnswers(Guid tenantId, IReadOnlyCollection<Guid> ticketIds);
    }
}
