namespace Services.Repositories.Data.BlogData
{
    public class BlogPostSection
    {
        public int Id { get; set; }
        public int BlogPostId { get; set; }
        public string? SectionTitle { get; set; }
        public string? SectionText { get; set; }
        public string? SectionMediaUrl { get; set; }
        public int? SectionMediaTypeId { get; set; }
        public string? SectionMediaPosition { get; set; }
        public string? SectionMediaText { get; set; }
        public string? SectionMediaWidth { get; set; }
        public int SortOrder { get; set; }
    }

    public enum BlogSectionMediaType
    {
        UploadedImage = 1,
        ExternalImageUrl = 2,
        YouTube = 3
    }
}
