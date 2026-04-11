namespace Services.Repositories.Data.BlogData
{
    public class BlogFeed
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string? Url { get; set; }
        public string? Description { get; set; }
        public string? CoverImageUrl { get; set; }
        public string CreatedByUserId { get; set; }
        public string? CreatedByUser { get; set; }
        public DateTime CreatedDate { get; set; }
    }
}
