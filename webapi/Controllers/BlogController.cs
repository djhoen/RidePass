using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using webapi.Models;
using Services.Helpers;
using Services.Repositories.Interfaces;
using Services.Repositories.Data.BlogData;

namespace webapi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class BlogController : ControllerBase
    {
        private readonly IBlogRepository _blogRepository;

        public BlogController(IBlogRepository blogRepository)
        {
            _blogRepository = blogRepository;
        }

        [HttpGet("GetBlogFeeds")]
        public async Task<IActionResult> GetBlogFeeds()
        {
            try
            {
                var feeds = await _blogRepository.GetBlogFeeds();

                return new ApiResponses().OkResult(feeds);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        [HttpGet("GetBlogFeed")]
        public async Task<IActionResult> GetBlogFeed([FromQuery] int id)
        {
            try
            {
                var feed = await _blogRepository.GetBlogFeed(id);

                return new ApiResponses().OkResult(feed);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        [HttpGet("GetBlogFeedByUrl")]
        public async Task<IActionResult> GetBlogFeedByUrl([FromQuery] string url)
        {
            try
            {
                var feed = await _blogRepository.GetBlogFeedByUrl(url);

                return new ApiResponses().OkResult(feed);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        [HttpGet("GetBlogPost")]
        public async Task<IActionResult> GetBlogPost([FromQuery] int id)
        {
            try
            {
                var post = await _blogRepository.GetBlogPost(id);

                return new ApiResponses().OkResult(post);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        [HttpGet("GetBlogPostByUrl")]
        public async Task<IActionResult> GetBlogPostByUrl([FromQuery] string url)
        {
            try
            {
                var post = await _blogRepository.GetBlogPost(url);

                return new ApiResponses().OkResult(post);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        [HttpGet("GetBlogPosts")]
        public async Task<IActionResult> GetBlogPosts([FromQuery] int feedId, [FromQuery] bool publishedOnly = true)
        {
            try
            {
                var posts = await _blogRepository.GetBlogPosts(feedId, publishedOnly);

                return new ApiResponses().OkResult(posts);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        [HttpGet("GetBlogPostSections")]
        public async Task<IActionResult> GetBlogPostSections([FromQuery] int postId)
        {
            try
            {
                var sections = await _blogRepository.GetBlogPostSections(postId);

                return new ApiResponses().OkResult(sections);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        [Authorize(Roles = "SuperAdmin,Admin")]
        [HttpPost("CreateBlogFeed")]
        public async Task<IActionResult> CreateBlogFeed([FromBody] BlogFeedRequest request)
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                var blogFeed = new BlogFeed
                {
                    Title = request.Title,
                    Url = request.Url,
                    Description = request.Description,
                    CoverImageUrl = request.CoverImageUrl,
                    CreatedByUserId = userId
                };

                var result = await _blogRepository.CreateBlogFeed(blogFeed);

                return new ApiResponses().OkResult(result);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        [Authorize(Roles = "SuperAdmin,Admin")]
        [HttpPost("UpdateBlogFeed")]
        public async Task<IActionResult> UpdateBlogFeed([FromBody] BlogFeedRequest request)
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                var blogFeed = new BlogFeed
                {
                    Id = request.Id ?? 0,
                    Title = request.Title,
                    Url = request.Url,
                    Description = request.Description,
                    CoverImageUrl = request.CoverImageUrl
                };

                await _blogRepository.UpdateBlogFeed(blogFeed);

                return new ApiResponses().OkResult(blogFeed);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        [Authorize(Roles = "SuperAdmin,Admin")]
        [HttpPost("CreateBlogPost")]
        public async Task<IActionResult> CreateBlogPost([FromBody] BlogPostRequest request)
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                var blogPost = new BlogPost
                {
                    Title = request.Title,
                    Url = request.Url,
                    Summary = request.Summary,
                    SummaryImgUrl = request.SummaryImgUrl,
                    Published = request.Published,
                    ShowAuthorInfo = request.ShowAuthorInfo,
                    AuthorUserId = userId
                };

                var result = await _blogRepository.CreateBlogPost(blogPost);

                return new ApiResponses().OkResult(result);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        [Authorize(Roles = "SuperAdmin,Admin")]
        [HttpPost("UpdateBlogPost")]
        public async Task<IActionResult> UpdateBlogPost([FromBody] BlogPostRequest request)
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                var blogPost = new BlogPost
                {
                    Id = request.Id ?? 0,
                    Title = request.Title,
                    Url = request.Url,
                    Summary = request.Summary,
                    SummaryImgUrl = request.SummaryImgUrl,
                    Published = request.Published,
                    ShowAuthorInfo = request.ShowAuthorInfo,
                    AuthorUserId = userId
                };

                await _blogRepository.UpdateBlogPost(blogPost);

                return new ApiResponses().OkResult(blogPost);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        [Authorize(Roles = "SuperAdmin,Admin")]
        [HttpPost("CreateBlogPostSection")]
        public async Task<IActionResult> CreateBlogPostSection([FromBody] BlogPostSectionRequest request)
        {
            try
            {
                var section = new BlogPostSection
                {
                    BlogPostId = request.BlogPostId,
                    SectionTitle = request.SectionTitle,
                    SectionText = request.SectionText,
                    SectionMediaUrl = request.SectionMediaUrl,
                    SectionMediaTypeId = request.SectionMediaTypeId,
                    SectionMediaPosition = request.SectionMediaPosition,
                    SectionMediaText = request.SectionMediaText,
                    SectionMediaWidth = request.SectionMediaWidth,
                    SortOrder = request.SortOrder
                };

                var result = await _blogRepository.CreateBlogPostSection(section);

                return new ApiResponses().OkResult(result);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        [Authorize(Roles = "SuperAdmin,Admin")]
        [HttpPost("UpdateBlogPostSection")]
        public async Task<IActionResult> UpdateBlogPostSection([FromBody] BlogPostSectionRequest request)
        {
            try
            {
                var section = new BlogPostSection
                {
                    Id = request.Id ?? 0,
                    BlogPostId = request.BlogPostId,
                    SectionTitle = request.SectionTitle,
                    SectionText = request.SectionText,
                    SectionMediaUrl = request.SectionMediaUrl,
                    SectionMediaTypeId = request.SectionMediaTypeId,
                    SectionMediaPosition = request.SectionMediaPosition,
                    SectionMediaText = request.SectionMediaText,
                    SectionMediaWidth = request.SectionMediaWidth,
                    SortOrder = request.SortOrder
                };

                await _blogRepository.UpdateBlogPostSection(section);

                return new ApiResponses().OkResult(section);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        [Authorize(Roles = "SuperAdmin,Admin")]
        [HttpPost("DeleteBlogPostSection")]
        public async Task<IActionResult> DeleteBlogPostSection([FromBody] DeleteRequest request)
        {
            try
            {
                await _blogRepository.DeleteBlogPostSection(request.Id);

                return new ApiResponses().OkResult(null);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        [Authorize(Roles = "SuperAdmin,Admin")]
        [HttpPost("AddPostToFeed")]
        public async Task<IActionResult> AddPostToFeed([FromBody] BlogFeedItemRequest request)
        {
            try
            {
                var feedItem = new BlogFeedItem
                {
                    BlogFeedId = request.BlogFeedId,
                    PostId = request.PostId
                };

                var result = await _blogRepository.AddPostToFeed(feedItem);

                return new ApiResponses().OkResult(result);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }

        [Authorize(Roles = "SuperAdmin,Admin")]
        [HttpPost("DeleteBlogFeedItem")]
        public async Task<IActionResult> DeleteBlogFeedItem([FromBody] DeleteRequest request)
        {
            try
            {
                await _blogRepository.DeleteBlogFeedItem(request.Id, request.ParentId ?? 0);

                return new ApiResponses().OkResult(null);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }
        }
    }
}
