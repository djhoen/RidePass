namespace webapi.Controllers.API.Data.LessonAssessment
{
    /// <summary>A ticket's self-assessment, for the admin order details and the Rider Report.</summary>
    public class TicketAssessmentDto
    {
        public Guid TicketId { get; set; }
        public string? RiderName { get; set; }
        public string SurveyTitle { get; set; } = string.Empty;
        public DateTime SubmittedAtUtc { get; set; }
        public List<AssessmentAnswerDto> Answers { get; set; } = new();
    }
}
