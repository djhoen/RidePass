using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Survey
{
    public class SendSurveyInvitesRequest
    {
        [Required]
        public AudienceCriteria Audience { get; set; } = null!;
    }
}
