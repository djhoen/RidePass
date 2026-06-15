namespace webapi.Controllers.API.Data.Concession
{
    public class ReorderConcessionProductsRequest
    {
        public List<ReorderItem> Items { get; set; } = new();

        public class ReorderItem
        {
            public Guid Id { get; set; }
            public int SortOrder { get; set; }
        }
    }
}
