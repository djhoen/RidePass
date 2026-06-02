namespace Services.Repositories.Data.MessagingData
{
    public class TenantMessage
    {
        public Guid Id { get; set; }
        public Guid ConversationId { get; set; }
        public Guid TenantId { get; set; }
        public string Direction { get; set; } = null!;  // 'inbound' | 'outbound'
        public string Body { get; set; } = null!;
        public string? TwilioMessageSid { get; set; }
        public string Status { get; set; } = null!;
        public int? NumSegments { get; set; }
        public Guid? SentByUserId { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
