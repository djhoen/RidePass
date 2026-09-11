using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Newsletter
{
    public class UpsertAudienceRequest
    {
        [Required, StringLength(120, MinimumLength = 1)]
        public string Name { get; set; } = string.Empty;
        [StringLength(500)]
        public string? Description { get; set; }
        [Required]
        public AudienceDefinitionDto Definition { get; set; } = new();
    }
}
