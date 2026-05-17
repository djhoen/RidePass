using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Event
{
    public class SubscribeEventsRequest
    {
        [Required, EmailAddress, MaxLength(200)]
        public string Email { get; set; } = null!;

        [MaxLength(40)]
        public string? Phone { get; set; }

        public bool NotifyEmail { get; set; } = true;
        public bool NotifySms { get; set; } = false;
    }

    public class EventSubscriptionStatusResponse
    {
        public bool Subscribed { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public bool NotifyEmail { get; set; }
        public bool NotifySms { get; set; }
        public string TenantDisplayName { get; set; } = null!;
    }
}
