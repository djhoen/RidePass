using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Inbox
{
    public class SendReplyRequest
    {
        [Required]
        [MaxLength(1600)]  // ~10 SMS segments — soft limit before we should be a campaign instead.
        public string Body { get; set; } = null!;
    }
}
