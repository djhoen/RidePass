namespace Services.Repositories.Data.TenantData
{
    public class TenantGalleryImage
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string ImageUrl { get; set; } = null!;
        public string? Caption { get; set; }
        public int SortOrder { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
