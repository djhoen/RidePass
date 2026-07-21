namespace webapi.Controllers.API.Data.QuickBooks
{
    /// <summary>One account from the tenant's own chart of accounts, for the mapping dropdowns.</summary>
    public class QboAccountResponse
    {
        public string Id { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string AccountType { get; set; } = null!;
        public string? AccountSubType { get; set; }
        /// <summary>Revenue / Asset / Liability / Expense. Used to filter the list per slot.</summary>
        public string? Classification { get; set; }
    }
}
