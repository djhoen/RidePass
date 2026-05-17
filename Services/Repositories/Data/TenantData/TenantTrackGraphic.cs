namespace Services.Repositories.Data.TenantData
{
    public class TenantTrackGraphic
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string ImageUrl { get; set; } = null!;
        public string? Title { get; set; }
        public string? Description { get; set; }
        public int SortOrder { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
