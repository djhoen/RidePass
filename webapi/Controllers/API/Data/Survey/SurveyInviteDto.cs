namespace webapi.Controllers.API.Data.Survey
{
    public class SurveyInviteDto
    {
        public Guid Id { get; set; }
        public string Email { get; set; } = null!;
        public DateTime? SentAtUtc { get; set; }
        public DateTime? OpenedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }
}
