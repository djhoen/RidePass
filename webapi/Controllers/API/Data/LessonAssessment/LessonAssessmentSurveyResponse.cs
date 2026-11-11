using webapi.Controllers.API.Data.Survey;

namespace webapi.Controllers.API.Data.LessonAssessment
{
    /// <summary>The self-assessment an event asks once per rider at checkout (Script0318): the survey's questions only.</summary>
    public class LessonAssessmentSurveyResponse
    {
        public Guid SurveyId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public List<SurveyQuestionDto> Questions { get; set; } = new();
    }
}
