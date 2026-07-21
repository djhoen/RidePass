namespace Services.Repositories.Data.QuickBooksData
{
    /// <summary>
    /// Maps one <see cref="Services.Accounting.QboAccountKeys"/> slot onto an account in the
    /// tenant's own chart of accounts (qbo_account_mapping).
    /// </summary>
    public class QboAccountMapping
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        /// <summary>A QboAccountKeys constant, e.g. "revenue_concession".</summary>
        public string MappingKey { get; set; } = null!;
        /// <summary>The QBO Account.Id. The only value the sync itself trusts.</summary>
        public string QboAccountId { get; set; } = null!;
        /// <summary>Display snapshot so the settings screen renders without a QBO round-trip.</summary>
        public string? QboAccountName { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
