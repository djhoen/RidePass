namespace webapi.Controllers.API.Data.LessonAssessment
{
    /// <summary>A survey an admin can pick as an event type's self-assessment.</summary>
    public class AssessmentSurveyOption
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }
}
