namespace Services.Repositories.Data.EmailData
{
    public class EmailSuppression
    {
        public Guid Id { get; set; }
        public Guid? TenantId { get; set; }   // null = platform-wide
        public string Email { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;   // bounce | complaint | unsubscribe | manual
        public string Scope { get; set; } = string.Empty;    // all | marketing
        public string? Source { get; set; }
        public string? Detail { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
