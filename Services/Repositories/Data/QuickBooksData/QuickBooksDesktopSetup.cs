namespace Services.Repositories.Data.QuickBooksData
{
    /// <summary>
    /// A tenant's QuickBooks Desktop setup (tenant_quickbooks_desktop). The Desktop counterpart of
    /// <see cref="QuickBooksConnection"/>: there is no OAuth link, only the date exports start from
    /// and when the track last uploaded its lists. A tenant has this OR an Online connection, never both.
    /// </summary>
    public class QuickBooksDesktopSetup
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        /// <summary>Nothing before this business date is ever exported.</summary>
        public DateOnly SyncStartDate { get; set; }
        /// <summary>The PROD value from the uploaded file's HDR row, e.g. "QuickBooks Pro".</summary>
        public string? CompanyProduct { get; set; }
        public DateTime? ListsUploadedAtUtc { get; set; }
        public Guid? CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
