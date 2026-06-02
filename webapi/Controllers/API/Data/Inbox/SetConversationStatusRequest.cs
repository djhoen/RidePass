using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Inbox
{
    public class SetConversationStatusRequest
    {
        [Required]
        [RegularExpression("^(active|archived)$",
            ErrorMessage = "Status must be 'active' or 'archived'.")]
        public string Status { get; set; } = null!;
    }
}
