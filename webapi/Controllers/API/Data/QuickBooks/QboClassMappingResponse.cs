namespace webapi.Controllers.API.Data.QuickBooks
{
    /// <summary>
    /// One reporting bucket and the QuickBooks Class it posts under. Buckets are resolved
    /// server-side from the tenant's profit centers (or the built-in departments when they have
    /// none), so the settings screen never has to reproduce that fallback rule.
    /// </summary>
    public class QboClassMappingResponse
    {
        /// <summary>"pc:&lt;guid&gt;" for a configured center, else a built-in department key.</summary>
        public string BucketKey { get; set; } = null!;
        public string Label { get; set; } = null!;
        /// <summary>#RRGGBB, the same color this bucket is drawn in on every report and chart.</summary>
        public string Color { get; set; } = null!;
        /// <summary>False when this is a built-in department rather than a center the tenant named.</summary>
        public bool IsCustom { get; set; }
        /// <summary>Human labels of the revenue streams that post under this bucket.</summary>
        public List<string> RevenueStreams { get; set; } = new();
        public string? QboClassId { get; set; }
        public string? QboClassName { get; set; }
    }
}
