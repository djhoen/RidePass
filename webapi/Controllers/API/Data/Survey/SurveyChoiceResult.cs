namespace webapi.Controllers.API.Data.Survey
{
    public class SurveyChoiceResult
    {
        public Guid ChoiceId { get; set; }
        public string Label { get; set; } = null!;
        public int Count { get; set; }
        public double Percent { get; set; }       // 0..100
        public bool AllowsFreeText { get; set; }
        // Populated only when AllowsFreeText: the explanations respondents
        // typed alongside this choice. Empty list otherwise.
        public List<string> FreeTextAnswers { get; set; } = new();
    }
}
