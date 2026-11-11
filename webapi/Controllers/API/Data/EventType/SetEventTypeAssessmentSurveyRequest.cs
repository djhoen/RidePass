namespace webapi.Controllers.API.Data.EventType
{
    /// <summary>The survey to ask as the rider self-assessment for an event type; null turns it off.</summary>
    public class SetEventTypeAssessmentSurveyRequest
    {
        public Guid? SurveyId { get; set; }
    }
}
