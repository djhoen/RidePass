namespace Services.Repositories.Data.BlogData
{
    public class BlogPost
    {
        public int Id { get; set; }
        public string AuthorUserId { get; set; }
        public string? AuthorUser { get; set; }
        public string? AuthorProfileImgUrl { get; set; }
        public string Title { get; set; }
        public string? Url { get; set; }
        public string? Summary { get; set; }
        public string? SummaryImgUrl { get; set; }
        public bool Published { get; set; }
        public bool ShowAuthorInfo { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? LastUpdatedDate { get; set; }
    }
}
