namespace Services.Repositories.Data.AccountingData
{
    /// <summary>
    /// A tenant-named revenue bucket ("Corp Tickets", "Training Center"). Revenue slots are
    /// assigned to centers via profit_center_revenue_key; a tenant with no centers at all uses the
    /// built-in <see cref="Services.Accounting.QboDepartments"/> grouping unchanged.
    /// </summary>
    public class ProfitCenter
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = null!;
        public int SortOrder { get; set; }
        /// <summary>
        /// #RRGGBB the center is drawn in everywhere it appears. Null only for a row written
        /// before Script0276's backfill; callers fall back to ProfitCenterPalette.Unassigned.
        /// </summary>
        public string? Color { get; set; }
    }
}
