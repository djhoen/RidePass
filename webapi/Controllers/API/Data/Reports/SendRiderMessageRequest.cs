using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Reports
{
    public class SendRiderMessageRequest
    {
        [Required]
        public List<Guid> PurchaseIds { get; set; } = new();

        // 'sms' or 'email'. Server validates against ISmsSender / ISmtpEmailer
        // configuration so the admin gets a clear error when the channel isn't
        // wired up for this deploy.
        [Required, RegularExpression("^(sms|email)$")]
        public string Channel { get; set; } = "sms";

        // Required when Channel='email'; ignored for SMS. Defaults to a
        // tenant-named subject if blank on email.
        [MaxLength(200)]
        public string? Subject { get; set; }

        [Required, MaxLength(2000)]
        public string Body { get; set; } = null!;

        // Null or in the past → send immediately and return per-row results.
        // In the future → enqueue a scheduled_task row and return its id; the
        // TaskRunner picks it up.
        public DateTime? RunAtUtc { get; set; }
    }

    public class SendRiderMessageResponse
    {
        // Immediate-send path.
        public int? Sent { get; set; }
        public int? Skipped { get; set; }
        public List<string>? SkippedNames { get; set; }

        // Scheduled path.
        public Guid? ScheduledTaskId { get; set; }
        public DateTime? ScheduledRunAtUtc { get; set; }
    }
}
