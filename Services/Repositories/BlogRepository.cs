using Services.Helpers.Interfaces;
using Services.Repositories.Data.BlogData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class BlogRepository : IBlogRepository
    {
        private readonly IDbHelper _dbHelper;
        public BlogRepository(IDbHelper doDbHelper)
        {
            _dbHelper = doDbHelper;
        }

        public async Task<BlogFeed> CreateBlogFeed(BlogFeed blogFeed)
        {
            var sql = @"INSERT INTO ""blog.feed"" (""title"", ""createdDate"", ""createdByUserId"", ""description"", ""coverImageUrl"", ""url"")
                        VALUES (@title, @createdDate, @createdByUserId, @description, @coverImageUrl, @url)
                        ON CONFLICT (""id"") DO NOTHING
                        RETURNING ""id""";

            var result = await _dbHelper.Query<int>(sql, blogFeed);

            blogFeed.Id = result.FirstOrDefault();

            return blogFeed;
        }

        public async Task<BlogFeedItem> AddPostToFeed(BlogFeedItem blogFeedItem)
        {
            var existingItemsSql = @"SELECT *
                                    FROM ""blog.feedItem""
                                    WHERE ""postId"" = @postId
                                        AND ""blogFeedId"" = @blogFeedId";

            var existingItemsResponse = await _dbHelper.Query<BlogFeedItem>(existingItemsSql, blogFeedItem);
            if (existingItemsResponse != null && existingItemsResponse.Count() > 0)
            {
                throw new Exception("Blog feed already contains this post");
            }
            else
            {
                var sql = @"INSERT INTO ""blog.feedItem"" (""blogFeedId"", ""postId"", ""published"")
                        VALUES (@blogFeedId, @postId, @published)
                        ON CONFLICT (""id"") DO NOTHING
                        RETURNING ""id""";

                var result = await _dbHelper.Query<int>(sql, blogFeedItem);

                blogFeedItem.Id = result.FirstOrDefault();

                return blogFeedItem;
            }
        }

        public async Task<BlogPost> CreateBlogPost(BlogPost blogPost)
        {
            var sql = @"INSERT INTO ""blog.post"" (""authorUserId"", ""createdDate"", ""published"", ""title"", ""url"", ""summary"", ""summaryImgUrl"", ""lastUpdatedDate"", ""showAuthorInfo"")
                        VALUES (@authorUserId, @createdDate, @published, @title, @url, @summary, @summaryImgUrl, @lastUpdatedDate, @showAuthorInfo)
                        ON CONFLICT (""id"") DO NOTHING
                        RETURNING ""id""";

            var result = await _dbHelper.Query<int>(sql, blogPost);

            blogPost.Id = result.FirstOrDefault();

            return blogPost;
        }

        public async Task<BlogPostSection> CreateBlogPostSection(BlogPostSection blogPostSection)
        {
            var sql = @"INSERT INTO ""blog.postSection"" (""blogPostId"", ""sectionTitle"", ""sectionText"", ""sectionMediaUrl"", ""sectionMediaTypeId"", ""sectionMediaPosition"", ""sectionMediaText"", ""sortOrder"", ""sectionMediaWidth"")
                        VALUES (@blogPostId, @sectionTitle, @sectionText, @sectionMediaUrl, @sectionMediaTypeId, @sectionMediaPosition, @sectionMediaText, @sortOrder, @sectionMediaWidth)
                        ON CONFLICT (""id"") DO NOTHING
                        RETURNING ""id""";

            var result = await _dbHelper.Query<int>(sql, blogPostSection);

            blogPostSection.Id = result.FirstOrDefault();

            return blogPostSection;
        }

        public async Task DeleteBlogFeedItem(int postId, int blogFeedId)
        {
            var sql = @"DELETE FROM ""blog.feedItem"" WHERE ""postId"" = @postId AND ""blogFeedId"" = @blogFeedId";

            await _dbHelper.Execute(sql, new { postId, blogFeedId });
        }

        public async Task DeleteBlogPostSection(int sectionId)
        {
            var sql = @"DELETE FROM ""blog.postSection"" WHERE ""id"" = @sectionId";

            await _dbHelper.Execute(sql, new { sectionId });
        }

        public async Task<BlogFeed> GetBlogFeed(int blogFeedId)
        {
            var sql = @"SELECT f.*, u.""displayName"" AS CreatedByUser
                        FROM ""blog.feed"" f
                            JOIN ""user"" u ON u.""id"" = f.""createdByUserId""
                        WHERE f.""id"" = @blogFeedId";

            var result = await _dbHelper.Query<BlogFeed>(sql, new { blogFeedId });
            return result.FirstOrDefault();
        }

        public async Task<BlogFeed> GetBlogFeedByUrl(string url)
        {
            var sql = @"SELECT f.*, u.""displayName"" AS CreatedByUser
                        FROM ""blog.feed"" f
                            JOIN ""user"" u ON u.""id"" = f.""createdByUserId""
                        WHERE LOWER(f.""url"") = LOWER(@url)";

            var result = await _dbHelper.Query<BlogFeed>(sql, new { url });
            return result.FirstOrDefault();
        }

        public async Task<List<BlogFeed>> GetBlogFeeds()
        {
            var sql = @"SELECT f.*, u.""displayName"" AS CreatedByUser
                        FROM ""blog.feed"" f
                            JOIN ""user"" u ON u.""id"" = f.""createdByUserId""";

            var result = await _dbHelper.Query<BlogFeed>(sql);
            return result.ToList();
        }

        public async Task<BlogPost> GetBlogPost(int blogPostId)
        {
            var sql = @"SELECT bp.*, u.""displayName"" as AuthorUser, u.""profileImgUrl"" AS AuthorProfileImgUrl
                        FROM ""blog.post"" bp
                            LEFT JOIN ""user"" u ON u.""id"" = bp.""authorUserId""
                        WHERE bp.""id"" = @blogPostId";

            var result = await _dbHelper.Query<BlogPost>(sql, new { blogPostId });
            return result.FirstOrDefault();
        }

        public async Task<BlogPost> GetBlogPost(string url)
        {
            var sql = @"SELECT bp.*, u.""displayName"" as AuthorUser, u.""profileImgUrl"" AS AuthorProfileImgUrl
                        FROM ""blog.post"" bp
                            LEFT JOIN ""user"" u ON u.""id"" = bp.""authorUserId""
                        WHERE LOWER(bp.""url"") = LOWER(@url)";

            var result = await _dbHelper.Query<BlogPost>(sql, new { url });
            return result.FirstOrDefault();
        }

        public async Task<List<BlogPost>> GetBlogPosts(int blogFeedId, bool publishedOnly = true)
        {
            var whereClause = string.Empty;
            if (publishedOnly)
            {
                whereClause = @"AND bp.""published"" = true";
            }

            var sql = $@"SELECT bp.*, u.""displayName"" as AuthorUser, u.""profileImgUrl"" AS AuthorProfileImgUrl
                        FROM ""blog.post"" bp
                            LEFT JOIN ""user"" u ON u.""id"" = bp.""authorUserId""
                        WHERE bp.""id"" = ANY(SELECT ""postId"" FROM ""blog.feedItem"" bfi WHERE bfi.""blogFeedId"" = @blogFeedId {whereClause})
                            ORDER BY bp.""createdDate"" DESC";

            var result = await _dbHelper.Query<BlogPost>(sql, new { blogFeedId });
            return result.ToList();
        }

        public async Task<BlogPostSection> GetBlogPostSection(int blogPostSectionId)
        {
            var sql = @"SELECT *
                        FROM ""blog.postSection""
                        WHERE ""id"" = @blogPostSectionId";

            var result = await _dbHelper.Query<BlogPostSection>(sql, new { blogPostSectionId });
            return result.FirstOrDefault();
        }

        public async Task<List<BlogPostSection>> GetBlogPostSections(int blogPostId)
        {
            var sql = @"SELECT *
                        FROM ""blog.postSection""
                        WHERE ""blogPostId"" = @blogPostId
                        ORDER BY ""sortOrder""";

            var result = await _dbHelper.Query<BlogPostSection>(sql, new { blogPostId });
            return result.ToList();
        }

        public async Task<List<BlogFeed>> GetSubscribedBlogFeeds(int postId)
        {
            var sql = @"SELECT f.*, u.""displayName"" AS CreatedByUser
                        FROM ""blog.feed"" f
                            LEFT JOIN ""user"" u ON u.""id"" = f.""createdByUserId""
                            LEFT JOIN ""blog.feedItem"" fi ON fi.""blogFeedId"" = f.""id""
                        WHERE fi.""postId"" = @postId";

            var result = await _dbHelper.Query<BlogFeed>(sql, new { postId });

            return result.ToList();
        }

        public async Task UpdateBlogFeed(BlogFeed blogFeed)
        {
            var sql = @"UPDATE ""blog.feed""
                        SET ""title"" = @title,
                            ""description"" = @description,
                            ""url"" = @url,
                            ""coverImageUrl"" = @coverImageUrl
                        WHERE ""id"" = @id";

            await _dbHelper.Execute(sql, blogFeed);
        }

        public async Task UpdateBlogPost(BlogPost blogPost)
        {
            blogPost.LastUpdatedDate = DateTime.Now;
            var sql = @"UPDATE ""blog.post""
                        SET ""published"" = @published,
                            ""title"" = @title,
                            ""summary"" = @summary,
                            ""summaryImgUrl"" = @summaryImgUrl,
                            ""lastUpdatedDate"" = @lastUpdatedDate,
                            ""showAuthorInfo"" = @showAuthorInfo,
                            ""url"" = @url
                        WHERE ""id"" = @id";

            await _dbHelper.Execute(sql, blogPost);
        }

        public async Task UpdateBlogPostSection(BlogPostSection blogPostSection)
        {
            var blogPost = await GetBlogPost(blogPostSection.BlogPostId);
            blogPost.LastUpdatedDate = DateTime.Now;
            await UpdateBlogPost(blogPost);

            var sql = @"UPDATE ""blog.postSection""
                        SET ""sectionTitle"" = @sectionTitle,
                            ""sectionText"" = @sectionText,
                            ""sectionMediaUrl"" = @sectionMediaUrl,
                            ""sectionMediaTypeId"" = @sectionMediaTypeId,
                            ""sectionMediaPosition"" = @sectionMediaPosition,
                            ""sectionMediaText"" = @sectionMediaText,
                            ""sectionMediaWidth"" = @sectionMediaWidth,
                            ""sortOrder"" = @sortOrder
                        WHERE ""id"" = @id";

            await _dbHelper.Execute(sql, blogPostSection);
        }
    }
}
