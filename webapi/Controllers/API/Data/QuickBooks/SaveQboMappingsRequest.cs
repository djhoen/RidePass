namespace webapi.Controllers.API.Data.QuickBooks
{
    /// <summary>Replace the tenant's account mapping. Slots omitted or sent null are cleared.</summary>
    public class SaveQboMappingsRequest
    {
        public List<QboMappingItem> Mappings { get; set; } = new();
    }

    public class QboMappingItem
    {
        public string MappingKey { get; set; } = null!;
        public string? QboAccountId { get; set; }
        public string? QboAccountName { get; set; }
    }
}
