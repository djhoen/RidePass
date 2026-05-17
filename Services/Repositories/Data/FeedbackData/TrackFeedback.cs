namespace Services.Repositories.Data.FeedbackData
{
    public class TrackFeedback
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid? UserId { get; set; }
        public string Name { get; set; } = null!;
        public string Email { get; set; } = null!;
        public int? Rating { get; set; }                   // 1..5 or null
        public string Body { get; set; } = null!;
        public string Status { get; set; } = "new";        // 'new' | 'addressed' | 'dismissed'
        public string? AdminNotes { get; set; }
        public Guid? ActionedByUserId { get; set; }
        public DateTime? ActionedAtUtc { get; set; }
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
