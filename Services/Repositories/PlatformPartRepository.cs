using Services.Helpers.Interfaces;
using Services.Repositories.Data.BikeShopData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    /// <inheritdoc cref="IPlatformPartRepository"/>
    public class PlatformPartRepository : IPlatformPartRepository
    {
        private readonly IDbHelper _db;

        public PlatformPartRepository(IDbHelper db)
        {
            _db = db;
        }

        // No tenant_id predicate here and that is correct: platform_part is shared identity by
        // design (Script0248). The row carries no tenant's data to leak.
        public async Task<PlatformPart?> GetByGtin(string gtin14)
        {
            // TimesConfirmed is DERIVED, not stored: see Script0248. Counting on the confirmation
            // table's primary-key prefix is cheap and cannot drift out of step with reality.
            const string sql = @"
                SELECT p.id AS Id, p.gtin14 AS Gtin14, p.name AS Name, p.brand AS Brand, p.mpn AS Mpn,
                       p.category_hint AS CategoryHint, p.source AS Source,
                       (SELECT count(*)::int FROM platform_part_confirmation c
                        WHERE c.platform_part_id = p.id) AS TimesConfirmed,
                       p.created_at AS CreatedAt, p.updated_at AS UpdatedAt
                FROM platform_part p
                WHERE p.gtin14 = @gtin14";
            return (await _db.Query<PlatformPart>(sql, new { gtin14 })).FirstOrDefault();
        }

        public async Task<Guid> Confirm(Guid tenantId, string gtin14, string name, string? brand,
            string? mpn, string? categoryHint)
        {
            // One statement so a scan cannot half-succeed: upsert the identity and record this
            // tenant's confirmation together. Idempotent per shop via the confirmation table's
            // composite primary key, so a busy counter scanning the same part all day is one row.
            //
            // There is deliberately no "bump a counter" CTE here. That was tried and is silently
            // broken: every CTE in a statement reads the same snapshot, so an UPDATE cannot see the
            // row a sibling CTE just inserted and the bump matches nothing. The count is derived on
            // read instead (see GetByGtin).
            const string sql = @"
                WITH upserted AS (
                    INSERT INTO platform_part (gtin14, name, brand, mpn, category_hint, source)
                    VALUES (@gtin14, @name, @brand, @mpn, @categoryHint, 'tenant_confirmed')
                    ON CONFLICT (gtin14) DO UPDATE
                        -- COALESCE fills blanks only. A later shop can complete a sparse entry but
                        -- never rewrite one, so the first shop to describe a part keeps the naming
                        -- and a typo can't propagate over an established row. `source` is likewise
                        -- never rewritten: a vendor-sourced row must keep naming its vendor or the
                        -- licensing purge would miss it.
                        SET brand         = COALESCE(platform_part.brand, EXCLUDED.brand),
                            mpn           = COALESCE(platform_part.mpn, EXCLUDED.mpn),
                            category_hint = COALESCE(platform_part.category_hint, EXCLUDED.category_hint),
                            updated_at    = now()
                    RETURNING id
                ),
                confirmed AS (
                    INSERT INTO platform_part_confirmation (platform_part_id, tenant_id)
                    SELECT id, @tenantId FROM upserted
                    ON CONFLICT DO NOTHING
                    RETURNING platform_part_id
                )
                SELECT id FROM upserted";
            return (await _db.Query<Guid>(sql,
                new { tenantId, gtin14, name, brand, mpn, categoryHint })).First();
        }

        public async Task<PlatformPart> CacheFromVendor(string sourceSlug, Services.BikeShop.PartLookupResult result)
        {
            // DO NOTHING rather than DO UPDATE on conflict: if a shop has already confirmed this
            // barcode, their word outranks the vendor's and must not be overwritten. The trailing
            // read then returns whichever row won, so a race between two registers scanning the
            // same new code still yields one row and both callers see it.
            const string sql = @"
                INSERT INTO platform_part (gtin14, name, brand, mpn, category_hint, source)
                VALUES (@Gtin14, @Name, @Brand, @Mpn, @CategoryHint, @sourceSlug)
                ON CONFLICT (gtin14) DO NOTHING";
            await _db.Execute(sql, new
            {
                result.Gtin14, result.Name, result.Brand, result.Mpn, result.CategoryHint, sourceSlug,
            });
            return (await GetByGtin(result.Gtin14))!;
        }

        public Task<int> PurgeSource(string source)
        {
            // Confirmations cascade; shop_variant.platform_part_id is ON DELETE SET NULL, so a
            // shop keeps its own product and simply loses the link to shared identity.
            const string sql = "DELETE FROM platform_part WHERE source = @source";
            return _db.Execute(sql, new { source });
        }

        public async Task<List<PlatformPartSourceCount>> CountsBySource()
        {
            const string sql = @"
                SELECT source AS Source, count(*)::int AS PartCount
                FROM platform_part
                GROUP BY source
                ORDER BY count(*) DESC";
            return (await _db.Query<PlatformPartSourceCount>(sql)).ToList();
        }
    }
}
