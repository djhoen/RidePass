namespace webapi.Controllers.API.Data.Survey
{
    public class SurveyQuestionDto
    {
        public Guid Id { get; set; }
        public string Kind { get; set; } = null!;
        public string Prompt { get; set; } = null!;
        public int SortOrder { get; set; }
        public bool Required { get; set; }
        public List<SurveyChoiceDto> Choices { get; set; } = new();
    }
}
