namespace Services.Repositories.Data.BlogData
{
    public class BlogFeedItem
    {
        public int Id { get; set; }
        public int BlogFeedId { get; set; }
        public int PostId { get; set; }
        public bool Published { get; set; }
    }
}
