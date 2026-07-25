using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Tenant
{
    /// <summary>Where and when staff may run money-moving operations. Both constraints are
    /// optional: an empty CIDR list means no location rule, and null hours mean no clock rule.</summary>
    public class UpdateStaffAccessPolicyRequest
    {
        /// <summary>0 = off, 1 = enforce.</summary>
        [Range(0, 1, ErrorMessage = "Mode must be 0 (off) or 1 (enforce).")]
        public int Mode { get; set; }

        /// <summary>Networks the track operates from, as CIDR blocks or bare addresses. Validated
        /// server-side; anything unparseable is rejected rather than silently ignored, because a
        /// typo here is what locks a track out of its own register.</summary>
        [MaxLength(50, ErrorMessage = "That's more networks than a track should need.")]
        public List<string> AllowedCidrs { get; set; } = new();

        /// <summary>Tenant-local "HH:mm". Both null or both set; an end at or before the start is
        /// a window that crosses midnight.</summary>
        public string? HoursStart { get; set; }
        public string? HoursEnd { get; set; }

        /// <summary>Email the tenant contact when the previous day's activity trips a rule.</summary>
        public bool AlertsEnabled { get; set; }

        /// <summary>One staffer's daily refund total above which the digest flags them.</summary>
        [Range(100, 100_000_00, ErrorMessage = "The refund alert threshold must be between $1 and $100,000.")]
        public int AlertRefundCents { get; set; } = 50000;
    }
}
