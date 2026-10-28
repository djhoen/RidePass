namespace webapi.Controllers.API.Data.QuickBooks
{
    /// <summary>What an uploaded QuickBooks Desktop list file replaced.</summary>
    public class QbdListUploadResponse
    {
        /// <summary>Null when the file carried no Chart of Accounts (the existing list was kept).</summary>
        public int? AccountCount { get; set; }
        /// <summary>Null when the file carried no Class List (the existing list was kept).</summary>
        public int? ClassCount { get; set; }
        public string? Product { get; set; }
        /// <summary>Mapped account or class names the new lists no longer contain.</summary>
        public List<string> MissingMappedNames { get; set; } = new();
    }
}
