namespace Services.Repositories.Data.QuickBooksData
{
    /// <summary>
    /// Maps one reporting bucket (a tenant profit center, or a built-in
    /// <see cref="Services.Accounting.QboDepartments"/> department) onto a Class in the tenant's
    /// QuickBooks company (qbo_class_mapping), so revenue posts split by profit center inside QBO
    /// rather than only inside RidePass.
    /// </summary>
    public class QboClassMapping
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        /// <summary>
        /// The bucket key <see cref="Services.Accounting.ProfitCenterMap"/> resolves a revenue slot
        /// to: "pc:&lt;guid&gt;" for a configured center, or a QboDepartments key for a fallback.
        /// </summary>
        public string BucketKey { get; set; } = null!;
        /// <summary>The QBO Class.Id. The only value the sync itself trusts.</summary>
        public string QboClassId { get; set; } = null!;
        /// <summary>Display snapshot so the settings screen renders without a QBO round-trip.</summary>
        public string? QboClassName { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
