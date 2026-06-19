namespace Services.TenantSync
{
    public class ExistingTenantState
    {
        public Guid Id { get; set; }
        public string Subdomain { get; set; } = null!;
        public bool EverPublished { get; set; }
        public int LiveOrderCount { get; set; }
    }

    public interface ITenantSyncRepository
    {
        /// <summary>
        /// Read every whitelisted config table for one tenant as type-faithful JSON
        /// (via row_to_json), keyed by table name. Read-only; used by the stage export.
        /// </summary>
        Task<Dictionary<string, string>> ExportTables(Guid tenantId);

        /// <summary>Latest applied DbUp migration (the schema fingerprint compared across envs).</summary>
        Task<string?> GetLatestSchemaVersion();

        /// <summary>
        /// Count money-bearing purchase/sale rows for a tenant (status paid/redeemed/refunded).
        /// Any &gt; 0 blocks an overwrite import, alongside the ever-published guard.
        /// </summary>
        Task<int> CountLiveOrders(Guid tenantId);

        /// <summary>
        /// Transactionally write a promoted tenant. tables holds the (already processed:
        /// URL-rewritten + tenant columns reset) config rows as JSON arrays keyed by table.
        /// replace=true first deletes the existing tenant (cascading its data). The tenant
        /// INSERT fires four seed triggers (branding/event_types/waiver/extra_products) whose
        /// rows are deleted before the bundle's versions are inserted in FK order.
        /// </summary>
        Task ImportTables(Guid tenantId, bool replace, IReadOnlyDictionary<string, string> tables);
    }
}
