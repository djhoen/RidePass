using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Survey
{
    public class ReorderQuestionsRequest
    {
        [Required, MinLength(1)]
        public List<ReorderQuestionItem> Items { get; set; } = new();
    }

    public class ReorderQuestionItem
    {
        [Required] public Guid Id { get; set; }
        public int SortOrder { get; set; }
    }
}
