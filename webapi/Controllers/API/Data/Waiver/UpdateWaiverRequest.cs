using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Waiver
{
    public class UpdateWaiverRequest
    {
        [Required, MaxLength(200)]
        public string Title { get; set; } = null!;

        [Required]
        public string Body { get; set; } = string.Empty;
    }
}
