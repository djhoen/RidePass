using Services.Helpers.Interfaces;

namespace Services.TenantSync
{
    public class TenantSyncRepository : ITenantSyncRepository
    {
        private readonly IDbHelper _db;

        public TenantSyncRepository(IDbHelper db) => _db = db;

        // Tables the four AFTER-INSERT tenant seed triggers populate; cleared on import so
        // the bundle's versions replace the auto-seeded defaults.
        private static readonly string[] SeededTables =
            { "tenant_branding", "tenant_event_type", "tenant_waiver", "event_extra_product" };

        public async Task ImportTables(Guid tenantId, bool replace, IReadOnlyDictionary<string, string> tables)
        {
            var stmts = new List<(string Sql, object? Param)>();

            if (replace)
            {
                // Cascades away the old tenant + all its data (guarded upstream: never
                // published, no live orders, so nothing real is lost).
                stmts.Add(("DELETE FROM tenant WHERE id = @id", new { id = tenantId }));
            }

            // Tenant row first — its AFTER-INSERT triggers seed default branding/event_types/
            // waiver/extra_products.
            if (tables.TryGetValue("tenant", out var tenantJson) && !string.IsNullOrWhiteSpace(tenantJson))
            {
                stmts.Add(("INSERT INTO tenant SELECT * FROM json_populate_recordset(NULL::tenant, @rows::json)",
                    new { rows = tenantJson }));
            }

            // Clear the trigger-seeded defaults so the bundle replaces them cleanly.
            foreach (var t in SeededTables)
            {
                stmts.Add(($"DELETE FROM {t} WHERE tenant_id = @id", new { id = tenantId }));
            }

            // Insert every other whitelisted table in FK order. json_populate_recordset
            // reconstructs each row from its JSON with full type fidelity.
            foreach (var (table, _) in TenantSyncTables.Config)
            {
                if (table == "tenant") continue;
                if (!tables.TryGetValue(table, out var json) || string.IsNullOrWhiteSpace(json) || json == "[]") continue;
                stmts.Add(($"INSERT INTO {table} SELECT * FROM json_populate_recordset(NULL::{table}, @rows::json)",
                    new { rows = json }));
            }

            await _db.ExecuteBatch(stmts);
        }

        public async Task<Dictionary<string, string>> ExportTables(Guid tenantId)
        {
            var result = new Dictionary<string, string>();
            foreach (var (table, scope) in TenantSyncTables.Config)
            {
                // Table/scope come from a hard-coded whitelist (never user input), so
                // interpolating them is safe; the tenant id stays parameterized. row_to_json
                // preserves Postgres types (uuid[], jsonb, timestamptz) across the round-trip.
                var sql = $"SELECT COALESCE(json_agg(row_to_json(t)), '[]'::json)::text FROM {table} t WHERE {scope}";
                var json = (await _db.Query<string>(sql, new { tenantId })).FirstOrDefault() ?? "[]";
                result[table] = json;
            }
            return result;
        }

        public async Task<string?> GetLatestSchemaVersion()
        {
            // DbUp's journal. Script names are zero-padded (Script0001..), so lexical max = latest.
            const string sql = "SELECT scriptname FROM schemaversions ORDER BY scriptname DESC LIMIT 1";
            return (await _db.Query<string>(sql)).FirstOrDefault();
        }

        public async Task<int> CountLiveOrders(Guid tenantId)
        {
            // The ever-published flag is the primary guard; this catches the rarer
            // never-published-but-took-money case. Counts money-moved rows only.
            const string sql = @"
                SELECT
                    (SELECT count(*) FROM event_ticket_purchase WHERE tenant_id = @tenantId AND status IN ('paid','redeemed','refunded'))
                  + (SELECT count(*) FROM event_extra_purchase  WHERE tenant_id = @tenantId AND status IN ('paid','redeemed','refunded'))
                  + (SELECT count(*) FROM season_pass_purchase  WHERE tenant_id = @tenantId AND status IN ('paid','redeemed','refunded'))
                  + (SELECT count(*) FROM membership_purchase   WHERE tenant_id = @tenantId AND status IN ('paid','redeemed','refunded'))
                  + (SELECT count(*) FROM concession_sale       WHERE tenant_id = @tenantId AND status IN ('paid','redeemed','refunded'))";
            return await _db.ExecuteScalar(sql, new { tenantId });
        }
    }
}
