namespace webapi.Controllers.API.Data.Survey
{
    /// <summary>
    /// Public-facing survey shape — no admin metadata. Returned for both the
    /// shared public token and per-recipient invite tokens.
    /// </summary>
    public class PublicSurveyResponse
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = null!;
        public string? Description { get; set; }
        public string Status { get; set; } = null!;
        public bool RequireEmail { get; set; }
        public DateTime? ClosesAtUtc { get; set; }

        // Set when the caller arrived via a per-recipient invite link. The fill
        // page can prefill the email and the submit endpoint will tie the
        // response back to the invite for tracking.
        public Guid? InviteToken { get; set; }
        public string? InviteEmail { get; set; }
        public bool? AlreadyCompleted { get; set; }

        public List<SurveyQuestionDto> Questions { get; set; } = new();
    }
}
