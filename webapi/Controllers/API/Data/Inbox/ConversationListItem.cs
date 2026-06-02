namespace webapi.Controllers.API.Data.Inbox
{
    /// <summary>
    /// One row in the admin Inbox conversation list. Trimmed shape — message
    /// bodies aren't included; the detail endpoint loads those on click.
    /// </summary>
    public class ConversationListItem
    {
        public Guid Id { get; set; }
        public string CustomerPhone { get; set; } = null!;
        public DateTime LastMessageAtUtc { get; set; }
        public DateTime? LastInboundAtUtc { get; set; }
        public DateTime? LastReadAtUtc { get; set; }
        public string Status { get; set; } = "active";
        public bool Unread { get; set; }
        public bool OptedOut { get; set; }

        public Guid? CustomerUserId { get; set; }
        public string? CustomerName { get; set; }
    }
}
