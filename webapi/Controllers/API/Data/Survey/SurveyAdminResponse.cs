namespace webapi.Controllers.API.Data.Survey
{
    /// <summary>
    /// Full admin view of a survey: editable metadata, questions, and choices.
    /// Used by the builder page.
    /// </summary>
    public class SurveyAdminResponse
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public string Title { get; set; } = null!;
        public string? Description { get; set; }
        public string Status { get; set; } = null!;
        public DateTime? ClosesAtUtc { get; set; }
        public bool RequireEmail { get; set; }
        public Guid PublicToken { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public List<SurveyQuestionDto> Questions { get; set; } = new();
    }
}
