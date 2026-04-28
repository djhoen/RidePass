namespace Services.Repositories.Data.NewsletterData
{
    public class NewsletterSubscriber
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Email { get; set; } = null!;
        public string? Name { get; set; }
        public string Source { get; set; } = "signup";
        public Guid UnsubscribeToken { get; set; }
        public DateTime SubscribedAt { get; set; }
        public DateTime? UnsubscribedAt { get; set; }
    }
}
