using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Survey
{
    /// <summary>
    /// Replaces all choices for a question in one shot. Order in the array
    /// becomes the saved sort_order. Simpler for the builder UI than per-choice CRUD.
    /// </summary>
    public class ReplaceChoicesRequest
    {
        [Required]
        public List<ChoiceInput> Choices { get; set; } = new();
    }
}
