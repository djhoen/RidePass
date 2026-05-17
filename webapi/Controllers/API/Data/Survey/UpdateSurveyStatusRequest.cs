using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Survey
{
    public class UpdateSurveyStatusRequest
    {
        [Required, RegularExpression("^(draft|published|closed)$")]
        public string Status { get; set; } = null!;
    }
}
