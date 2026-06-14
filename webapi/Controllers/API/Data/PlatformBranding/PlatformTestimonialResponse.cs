namespace webapi.Controllers.API.Data.PlatformBranding
{
    public class PlatformTestimonialResponse
    {
        public Guid Id { get; set; }
        public int SortOrder { get; set; }
        public string RiderName { get; set; } = null!;
        public string? RiderPhotoUrl { get; set; }
        public string Quote { get; set; } = null!;
        public int Rating { get; set; }
        public bool IsActive { get; set; }
    }
}
