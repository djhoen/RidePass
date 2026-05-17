namespace webapi.Controllers.API.Data.Survey
{
    public class SurveyChoiceDto
    {
        public Guid Id { get; set; }
        public string Label { get; set; } = null!;
        public int SortOrder { get; set; }
        public bool AllowsFreeText { get; set; }
    }
}
