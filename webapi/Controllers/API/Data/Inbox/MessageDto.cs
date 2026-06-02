namespace webapi.Controllers.API.Data.Inbox
{
    public class MessageDto
    {
        public Guid Id { get; set; }
        public string Direction { get; set; } = null!;
        public string Body { get; set; } = null!;
        public string Status { get; set; } = null!;
        public int? NumSegments { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }
}
