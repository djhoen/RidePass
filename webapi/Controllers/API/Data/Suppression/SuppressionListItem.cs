namespace webapi.Controllers.API.Data.Suppression
{
    public class SuppressionListItem
    {
        public Guid Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;   // bounce | complaint | unsubscribe | manual
        public string Scope { get; set; } = string.Empty;    // all | marketing
        public string? Source { get; set; }
        public string? Detail { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }
}
