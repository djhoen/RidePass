namespace Services.Repositories.Data.PlatformData
{
    /// <summary>
    /// One rider testimonial rendered on the apex landing page. Platform
    /// scoped, no tenant id; super admins curate the list.
    /// </summary>
    public class PlatformTestimonial
    {
        public Guid Id { get; set; }
        public int SortOrder { get; set; } = 100;
        public string RiderName { get; set; } = null!;
        public string? RiderPhotoUrl { get; set; }
        public string Quote { get; set; } = null!;
        public int Rating { get; set; } = 5;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }
}
