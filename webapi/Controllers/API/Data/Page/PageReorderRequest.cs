namespace webapi.Controllers.API.Data.Page
{
    /// <summary>Bulk reorder of the tenant's pages (drag-drop).</summary>
    public class PageReorderRequest
    {
        public List<PageReorderItem> Items { get; set; } = new();
    }
}
