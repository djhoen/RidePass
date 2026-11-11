namespace Services.Repositories.Data.SurveyData
{
    /// <summary>
    /// One answer row of a lesson self-assessment (Script0318), flattened with its ticket, survey and
    /// question so the admin screens can group it per ticket without further lookups. A response with
    /// no answer rows still yields one row (QuestionId null) so "answered nothing optional" shows.
    /// </summary>
    public class TicketAssessmentAnswer
    {
        public Guid TicketId { get; set; }
        public Guid ResponseId { get; set; }
        public Guid SurveyId { get; set; }
        public string SurveyTitle { get; set; } = string.Empty;
        public DateTime SubmittedAtUtc { get; set; }
        public string? RiderFirstName { get; set; }
        public string? RiderLastName { get; set; }
        public Guid? QuestionId { get; set; }
        public string? Prompt { get; set; }
        public string? Kind { get; set; }
        public int QuestionSortOrder { get; set; }
        public string? ChoiceLabel { get; set; }
        public int ChoiceSortOrder { get; set; }
        public string? FreeText { get; set; }
    }
}
