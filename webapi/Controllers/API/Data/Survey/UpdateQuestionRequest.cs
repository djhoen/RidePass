using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Survey
{
    public class UpdateQuestionRequest
    {
        [Required, MaxLength(1000)]
        public string Prompt { get; set; } = null!;

        public int SortOrder { get; set; } = 100;

        public bool Required { get; set; }
    }
}
