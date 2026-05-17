namespace webapi.Controllers.API.Data.EventType
{
    public class EventTypeResponse
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string Color { get; set; } = null!;
        public string? ImageUrl { get; set; }
        public int SortOrder { get; set; }
        public bool IsSystem { get; set; }
    }
}
