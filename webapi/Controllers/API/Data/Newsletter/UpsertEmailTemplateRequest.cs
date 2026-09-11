using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Newsletter
{
    public class UpsertEmailTemplateRequest
    {
        [Required, StringLength(120, MinimumLength = 1)]
        public string Name { get; set; } = string.Empty;
        public string? Subject { get; set; }
        public string? PreviewText { get; set; }
        [Required] public string BodyHtml { get; set; } = string.Empty;
    }
}
