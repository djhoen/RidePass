namespace Services.Repositories.Data.SurveyData
{
    public class Survey
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = null!;
        public string Title { get; set; } = null!;
        public string? Description { get; set; }
        public string Status { get; set; } = "draft";   // draft | published | closed
        public DateTime? ClosesAtUtc { get; set; }
        public bool RequireEmail { get; set; }
        public Guid PublicToken { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class SurveyQuestion
    {
        public Guid Id { get; set; }
        public Guid SurveyId { get; set; }
        public string Kind { get; set; } = null!;       // single_choice | multiple_choice | free_form
        public string Prompt { get; set; } = null!;
        public int SortOrder { get; set; } = 100;
        public bool Required { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class SurveyQuestionChoice
    {
        public Guid Id { get; set; }
        public Guid QuestionId { get; set; }
        public string Label { get; set; } = null!;
        public int SortOrder { get; set; } = 100;
        // When true, picking this choice prompts the respondent for free-form
        // text (the "Other — please explain" pattern). The answer row then
        // stores both choice_id and free_text.
        public bool AllowsFreeText { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class SurveyInvite
    {
        public Guid Id { get; set; }
        public Guid SurveyId { get; set; }
        public string Email { get; set; } = null!;
        public Guid Token { get; set; }
        public DateTime? SentAtUtc { get; set; }
        public DateTime? OpenedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class SurveyResponse
    {
        public Guid Id { get; set; }
        public Guid SurveyId { get; set; }
        public Guid? UserId { get; set; }
        public Guid? InviteId { get; set; }
        public string? RespondentEmail { get; set; }
        public string? RespondentName { get; set; }
        public DateTime SubmittedAtUtc { get; set; }
        public string? IpAddress { get; set; }
    }

    public class SurveyAnswer
    {
        public Guid Id { get; set; }
        public Guid ResponseId { get; set; }
        public Guid QuestionId { get; set; }
        public Guid? ChoiceId { get; set; }
        public string? FreeText { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
