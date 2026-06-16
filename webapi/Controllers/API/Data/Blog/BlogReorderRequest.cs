namespace webapi.Controllers.API.Data.Blog
{
    /// <summary>Bulk reorder of a post's gallery images (drag-drop).</summary>
    public class BlogReorderRequest
    {
        public List<BlogReorderItem> Items { get; set; } = new();
    }
}
