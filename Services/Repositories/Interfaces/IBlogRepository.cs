using Services.Repositories.Data.BlogData;

namespace Services.Repositories.Interfaces
{
    public interface IBlogRepository
    {
        Task<BlogFeed> CreateBlogFeed(BlogFeed blogFeed);
        Task<BlogFeedItem> AddPostToFeed(BlogFeedItem blogFeedItem);
        Task<BlogPost> CreateBlogPost(BlogPost blogPost);
        Task<BlogPostSection> CreateBlogPostSection(BlogPostSection blogPostSection);
        Task DeleteBlogFeedItem(int postId, int blogFeedId);
        Task DeleteBlogPostSection(int sectionId);
        Task<BlogFeed> GetBlogFeed(int blogFeedId);
        Task<BlogFeed> GetBlogFeedByUrl(string url);
        Task<List<BlogFeed>> GetBlogFeeds();
        Task<BlogPost> GetBlogPost(int blogPostId);
        Task<BlogPost> GetBlogPost(string url);
        Task<List<BlogPost>> GetBlogPosts(int blogFeedId, bool publishedOnly);
        Task<BlogPostSection> GetBlogPostSection(int blogPostSectionId);
        Task<List<BlogPostSection>> GetBlogPostSections(int blogPostId);
        Task<List<BlogFeed>> GetSubscribedBlogFeeds(int postId);
        Task UpdateBlogFeed(BlogFeed blog);
        Task UpdateBlogPost(BlogPost blogPost);
        Task UpdateBlogPostSection(BlogPostSection blogPostSectionItem);
    }
}
