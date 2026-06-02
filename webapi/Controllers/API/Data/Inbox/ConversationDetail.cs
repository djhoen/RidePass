namespace webapi.Controllers.API.Data.Inbox
{
    /// <summary>
    /// Full conversation payload for the thread view: the conversation
    /// metadata plus every message in created_at order. Used by GET
    /// /api/TenantConversation/{id}.
    /// </summary>
    public class ConversationDetail
    {
        public Guid Id { get; set; }
        public string CustomerPhone { get; set; } = null!;
        public DateTime LastMessageAtUtc { get; set; }
        public DateTime? LastInboundAtUtc { get; set; }
        public DateTime? LastReadAtUtc { get; set; }
        public string Status { get; set; } = "active";
        public bool OptedOut { get; set; }

        public Guid? CustomerUserId { get; set; }
        public string? CustomerName { get; set; }

        public List<MessageDto> Messages { get; set; } = new();
    }
}
