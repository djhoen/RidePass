namespace Services.Repositories.Data.MessagingData
{
    /// <summary>
    /// Inbox list-row projection: a <see cref="TenantConversation"/> plus the
    /// joined opt-out flag. Modeled as a flat class (not composed) because
    /// Dapper maps positional column results into a single object per row;
    /// composition would force a multi-mapper setup for one boolean.
    /// </summary>
    public class ConversationListRow
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string CustomerPhone { get; set; } = null!;
        public Guid? CustomerUserId { get; set; }
        public DateTime LastMessageAt { get; set; }
        public DateTime? LastInboundAt { get; set; }
        public DateTime? LastReadAt { get; set; }
        public string Status { get; set; } = "active";
        public DateTime CreatedAt { get; set; }
        public bool OptedOut { get; set; }

        public string? CustomerFirstName { get; set; }
        public string? CustomerLastName { get; set; }

        public string? CustomerName =>
            string.IsNullOrWhiteSpace(CustomerFirstName) && string.IsNullOrWhiteSpace(CustomerLastName)
                ? null
                : $"{CustomerFirstName} {CustomerLastName}".Trim();

        public bool IsUnread =>
            LastInboundAt.HasValue
            && (!LastReadAt.HasValue || LastInboundAt > LastReadAt);
    }
}
