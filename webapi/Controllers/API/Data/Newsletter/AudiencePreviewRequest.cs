using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Newsletter
{
    /// <summary>Count an unsaved definition while the builder is open.</summary>
    public class AudiencePreviewRequest
    {
        [Required]
        public AudienceDefinitionDto Definition { get; set; } = new();
    }
}
