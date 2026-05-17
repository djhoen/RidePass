namespace webapi.Controllers.API.Data.Survey
{
    public class SubmitSurveyAnswer
    {
        public Guid QuestionId { get; set; }

        // For single_choice: ChoiceIds has 0 or 1 entry.
        // For multiple_choice: 0+ entries.
        // For free_form: empty.
        public List<Guid>? ChoiceIds { get; set; }

        // For free_form only.
        public string? FreeText { get; set; }
    }
}
