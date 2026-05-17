namespace webapi.Controllers.API.Data.Survey
{
    public class SurveyListItem
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public string Title { get; set; } = null!;
        public string Status { get; set; } = null!;
        public DateTime? ClosesAtUtc { get; set; }
        public Guid PublicToken { get; set; }
        public int QuestionCount { get; set; }
        public int ResponseCount { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }
}
