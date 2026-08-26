namespace webapi.Controllers.API.Data.QuickBooks
{
    /// <summary>Replace the tenant's profit-center-to-class mapping. Buckets sent null are cleared.</summary>
    public class SaveQboClassMappingsRequest
    {
        public List<QboClassMappingItem> Mappings { get; set; } = new();
    }

    public class QboClassMappingItem
    {
        public string BucketKey { get; set; } = null!;
        public string? QboClassId { get; set; }
        public string? QboClassName { get; set; }
    }
}
