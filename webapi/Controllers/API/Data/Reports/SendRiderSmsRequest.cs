using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Reports
{
    public class SendRiderSmsRequest
    {
        // Purchase ids (any source) the admin selected on the report. The
        // server resolves each to a phone number.
        [Required]
        public List<Guid> PurchaseIds { get; set; } = new();

        [Required, MaxLength(800)]
        public string Body { get; set; } = null!;
    }

    public class SendRiderSmsResponse
    {
        public int Sent { get; set; }
        public int Skipped { get; set; }            // missing phone, send failure, etc.
        public List<string> SkippedNames { get; set; } = new();
    }
}
