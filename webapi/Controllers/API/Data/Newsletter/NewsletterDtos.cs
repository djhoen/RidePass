using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Newsletter
{
    public class SubscribeRequest
    {
        [Required, EmailAddress] public string Email { get; set; } = null!;
        public string? Name { get; set; }
    }

    public class AdminAddSubscriberRequest
    {
        [Required, EmailAddress] public string Email { get; set; } = null!;
        public string? Name { get; set; }
    }

    public class ImportSubscribersRequest
    {
        [Required] public string RawLines { get; set; } = null!; // one email (or email,name) per line
    }

    public class SubscriberListItem
    {
        public Guid Id { get; set; }
        public string Email { get; set; } = null!;
        public string? Name { get; set; }
        public string Source { get; set; } = null!;
        public DateTime SubscribedAtUtc { get; set; }
        public DateTime? UnsubscribedAtUtc { get; set; }
    }

    public class UnsubscribeStatusResponse
    {
        public string Email { get; set; } = null!;
        public string? Name { get; set; }
        public string TenantDisplayName { get; set; } = null!;
        public bool Unsubscribed { get; set; }
    }

    public class ImportSubscribersResponse
    {
        public int Added { get; set; }
        public int Reactivated { get; set; }
        public int Skipped { get; set; }
    }
}
