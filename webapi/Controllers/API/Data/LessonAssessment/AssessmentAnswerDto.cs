namespace webapi.Controllers.API.Data.LessonAssessment
{
    /// <summary>One question of a self-assessment with what the rider answered (choices joined, "Other" text appended).</summary>
    public class AssessmentAnswerDto
    {
        public string Prompt { get; set; } = string.Empty;
        public string Answer { get; set; } = string.Empty;
    }
}
