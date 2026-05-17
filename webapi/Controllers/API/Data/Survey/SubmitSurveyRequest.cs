using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Survey
{
    public class SubmitSurveyRequest
    {
        // Required when the survey has require_email = true OR when no invite
        // token was used. Optional otherwise.
        [MaxLength(120)]
        public string? RespondentName { get; set; }

        [EmailAddress, MaxLength(200)]
        public string? RespondentEmail { get; set; }

        // Optional invite token from the email link. When present, the response
        // is tied to that invite and stamped completed_at_utc.
        public Guid? InviteToken { get; set; }

        [Required]
        public List<SubmitSurveyAnswer> Answers { get; set; } = new();
    }
}
