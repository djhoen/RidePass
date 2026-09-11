namespace webapi.Controllers.API.Data.Newsletter
{
    public class EmailTemplateItem
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Subject { get; set; }
        public string? PreviewText { get; set; }
        public string BodyHtml { get; set; } = string.Empty;
        public DateTime UpdatedAtUtc { get; set; }
    }
}
