namespace webapi.Controllers.API.Data.QuickBooks
{
    /// <summary>One account slot and the account (if any) the tenant has mapped onto it.</summary>
    public class QboMappingResponse
    {
        /// <summary>A QboAccountKeys constant.</summary>
        public string MappingKey { get; set; } = null!;
        public string Label { get; set; } = null!;
        /// <summary>Which QBO classification this slot expects, so the UI can filter the dropdown.</summary>
        public string ExpectedClassification { get; set; } = null!;
        public string? QboAccountId { get; set; }
        public string? QboAccountName { get; set; }
    }
}
