using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Survey
{
    public class CreateQuestionRequest
    {
        [Required, RegularExpression("^(single_choice|multiple_choice|free_form)$")]
        public string Kind { get; set; } = null!;

        [Required, MaxLength(1000)]
        public string Prompt { get; set; } = null!;

        public int SortOrder { get; set; } = 100;

        public bool Required { get; set; }

        // Optional initial choices (single_choice/multiple_choice). Order
        // matches array order; ignored for free_form.
        public List<ChoiceInput>? Choices { get; set; }
    }
}
