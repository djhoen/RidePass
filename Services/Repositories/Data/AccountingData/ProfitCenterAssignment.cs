namespace Services.Repositories.Data.AccountingData
{
    /// <summary>One (revenue slot -> profit center) assignment for a tenant.</summary>
    public class ProfitCenterAssignment
    {
        public string RevenueKey { get; set; } = null!;
        public Guid ProfitCenterId { get; set; }
    }
}
