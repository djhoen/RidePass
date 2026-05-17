namespace webapi.Controllers.API.Data.Survey
{
    public class SurveyQuestionResult
    {
        public Guid QuestionId { get; set; }
        public string Kind { get; set; } = null!;
        public string Prompt { get; set; } = null!;
        public int AnsweredCount { get; set; }   // distinct responses that answered this question
        public List<SurveyChoiceResult> ChoiceResults { get; set; } = new();
        public List<string> FreeFormAnswers { get; set; } = new();
    }
}
