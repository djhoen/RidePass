using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Survey
{
    /// <summary>
    /// Describes the recipient set for a survey send. The server resolves the
    /// criteria into an email list, dedupes, and creates one tracked invite per
    /// address.
    /// </summary>
    public class AudienceCriteria
    {
        [Required, RegularExpression("^(custom|event|timeframe|all_customers|subscribers)$")]
        public string Type { get; set; } = null!;

        // For 'custom' — admin-picked emails (typically resolved client-side
        // from the customer picker).
        public List<string>? Emails { get; set; }

        // For 'event' — only ticket/extra purchasers for this event get the link.
        public Guid? EventId { get; set; }

        // For 'timeframe' — half-open [from, to) on created_at.
        public DateTime? FromUtc { get; set; }
        public DateTime? ToUtc { get; set; }
    }
}
