namespace Services.Repositories.Data.QuickBooksData
{
    /// <summary>
    /// One account or class from the Chart of Accounts / Class List a tenant uploaded from
    /// QuickBooks Desktop (qbd_list_item). The only names the Desktop mapping screen offers.
    /// </summary>
    public class QbdListItem
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        /// <summary>account | class</summary>
        public string ListKind { get; set; } = null!;
        /// <summary>"Parent:Child", exactly as an imported journal line must name it.</summary>
        public string FullName { get; set; } = null!;
        /// <summary>IIF ACCNTTYPE code (INC, OCASSET, ...). Null for classes.</summary>
        public string? AccountType { get; set; }
        public string? AccountNumber { get; set; }
        public bool IsHidden { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
