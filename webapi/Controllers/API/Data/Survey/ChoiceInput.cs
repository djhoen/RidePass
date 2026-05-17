using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Survey
{
    /// <summary>
    /// Input shape for creating/replacing question choices. Order in the array
    /// becomes sort_order. Set AllowsFreeText for "Other — please explain"
    /// style choices that capture an optional respondent explanation.
    /// </summary>
    public class ChoiceInput
    {
        [Required, MaxLength(500)]
        public string Label { get; set; } = null!;

        public bool AllowsFreeText { get; set; }
    }
}
