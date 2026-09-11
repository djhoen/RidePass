using Dapper;
using Services.Email;
using Services.Helpers.Interfaces;
using Services.Repositories.Data.NewsletterData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class AudienceRepository : IAudienceRepository
    {
        private readonly IDbHelper _db;

        public AudienceRepository(IDbHelper db) => _db = db;

        private const string Columns = @"
            id AS Id, tenant_id AS TenantId, name AS Name, description AS Description,
            definition::text AS Definition, is_sample AS IsSample, created_by_user_id AS CreatedByUserId,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        public async Task<List<Audience>> ListForTenant(Guid tenantId)
        {
            var sql = $"SELECT {Columns} FROM audience WHERE tenant_id = @tenantId ORDER BY lower(name)";
            return (await _db.Query<Audience>(sql, new { tenantId })).ToList();
        }

        public async Task<Audience?> GetById(Guid id, Guid tenantId)
        {
            var sql = $"SELECT {Columns} FROM audience WHERE id = @id AND tenant_id = @tenantId LIMIT 1";
            return (await _db.Query<Audience>(sql, new { id, tenantId })).FirstOrDefault();
        }

        public async Task<Guid> Create(Audience a)
        {
            const string sql = @"
                INSERT INTO audience (tenant_id, name, description, definition, is_sample, created_by_user_id)
                VALUES (@TenantId, @Name, @Description, @Definition::jsonb, @IsSample, @CreatedByUserId)
                RETURNING id";
            return (await _db.Query<Guid>(sql, a)).First();
        }

        public async Task Update(Audience a)
        {
            const string sql = @"
                UPDATE audience
                SET name = @Name, description = @Description, definition = @Definition::jsonb, updated_at = now()
                WHERE id = @Id AND tenant_id = @TenantId";
            await _db.Execute(sql, a);
        }

        public async Task<bool> Delete(Guid id, Guid tenantId)
        {
            var n = await _db.Execute("DELETE FROM audience WHERE id = @id AND tenant_id = @tenantId", new { id, tenantId });
            return n > 0;
        }

        public async Task<List<AudienceRecipient>> Evaluate(Guid tenantId, AudienceDefinition def)
        {
            var (sql, p) = AudienceQuery.Build(tenantId, def, await TodayStartUtc(tenantId));
            return (await _db.Query<AudienceRecipient>(sql, p)).ToList();
        }

        public async Task<int> Count(Guid tenantId, AudienceDefinition def)
        {
            var (sql, p) = AudienceQuery.Build(tenantId, def, await TodayStartUtc(tenantId));
            return (await _db.Query<int>($"SELECT COUNT(*)::int FROM ({sql}) q", p)).First();
        }

        public async Task<(int Joined, int Left)> RefreshMembers(Audience a)
        {
            var def = AudienceDefinition.Parse(a.Definition);
            var (sql, p) = AudienceQuery.Build(a.TenantId, def, await TodayStartUtc(a.TenantId));
            p.Add("audienceId", a.Id);

            // Someone already a member keeps their joined_at; someone who left and matches again
            // joins afresh, so an automation on "joins this audience" sees a new arrival.
            var joinSql = $@"
                WITH cur AS ({sql})
                INSERT INTO audience_member (tenant_id, audience_id, email, name, user_id)
                SELECT @tenantId, @audienceId, cur.email, cur.name, cur.userid FROM cur
                ON CONFLICT (audience_id, email) DO UPDATE
                    SET joined_at = CASE WHEN audience_member.left_at IS NULL THEN audience_member.joined_at ELSE now() END,
                        left_at   = NULL,
                        name      = COALESCE(EXCLUDED.name, audience_member.name),
                        user_id   = COALESCE(EXCLUDED.user_id, audience_member.user_id)
                RETURNING (xmax = 0) AS inserted";
            var joined = (await _db.Query<bool>(joinSql, p)).Count(x => x);

            var leaveSql = $@"
                UPDATE audience_member m
                SET left_at = now()
                WHERE m.audience_id = @audienceId AND m.tenant_id = @tenantId AND m.left_at IS NULL
                  AND NOT EXISTS (SELECT 1 FROM ({sql}) cur WHERE cur.email = m.email)";
            var left = await _db.Execute(leaveSql, p);
            return (joined, left);
        }

        public async Task<Dictionary<Guid, AudienceUsage>> UsageCounts(Guid tenantId)
        {
            const string sql = @"
                SELECT a.id AS AudienceId,
                       (SELECT COUNT(*)::int FROM email_campaign c
                        WHERE c.tenant_id = a.tenant_id AND c.audience_kind = 'audience'
                          AND c.audience_config->>'audienceId' = a.id::text
                          AND c.status IN ('draft', 'scheduled')) AS Campaigns,
                       (SELECT COUNT(*)::int FROM marketing_automation m
                        WHERE m.tenant_id = a.tenant_id AND m.trigger_kind = 'audience_joined'
                          AND m.trigger_config->>'audienceId' = a.id::text) AS Automations
                FROM audience a
                WHERE a.tenant_id = @tenantId";
            var rows = await _db.Query<AudienceUsage>(sql, new { tenantId });
            return rows.ToDictionary(r => r.AudienceId);
        }

        public async Task<int> SeedSamples(Guid tenantId, Guid? userId)
        {
            var added = 0;
            foreach (var (name, description, def) in Samples())
            {
                // Matched by name so a re-seed fills in what was deleted without duplicating.
                const string sql = @"
                    INSERT INTO audience (tenant_id, name, description, definition, is_sample, created_by_user_id)
                    VALUES (@tenantId, @name, @description, @definition::jsonb, true, @userId)
                    ON CONFLICT (tenant_id, lower(name)) DO NOTHING
                    RETURNING id";
                var id = (await _db.Query<Guid>(sql, new { tenantId, name, description, definition = def.ToJson(), userId })).FirstOrDefault();
                if (id != Guid.Empty) added++;
            }
            return added;
        }

        /// <summary>The starter set. Names are what a track would have typed; each shows one rule kind.</summary>
        private static IEnumerable<(string Name, string Description, AudienceDefinition Def)> Samples()
        {
            var jan1 = new DateTime(DateTime.UtcNow.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            yield return ("Newsletter subscribers", "Everyone on the newsletter list.",
                new AudienceDefinition { Base = AudienceBases.Subscribers });
            yield return ("All customers", "Anyone who has paid for a ticket or a pass.",
                new AudienceDefinition { Base = AudienceBases.Customers });
            yield return ("Season pass holders", "Holds a pass that is still valid. Buy a pass and you are in; let it lapse and you are out.",
                new AudienceDefinition { Base = AudienceBases.Everyone, Rules = { new AudienceRule { Kind = AudienceRuleKinds.PassHolder, ActiveOnly = true } } });
            yield return ("Bought an event ticket this year", "Anyone with a paid ticket to an event that starts this calendar year.",
                new AudienceDefinition { Base = AudienceBases.Everyone, Rules = { new AudienceRule { Kind = AudienceRuleKinds.EventPurchased, FromUtc = jan1 } } });
            yield return ("Yesterday's abandoned carts", "Started a checkout yesterday and never paid, and has not bought since. Refreshes every day.",
                new AudienceDefinition { Base = AudienceBases.Everyone, Rules = { new AudienceRule { Kind = AudienceRuleKinds.AbandonedCart, Days = 1 } } });
            yield return ("Subscribers who have never bought", "On the newsletter list with no ticket and no pass. The people to convert.",
                new AudienceDefinition
                {
                    Base = AudienceBases.Subscribers, Match = "all",
                    Rules =
                    {
                        new AudienceRule { Kind = AudienceRuleKinds.PassHolder, Negate = true },
                        new AudienceRule { Kind = AudienceRuleKinds.EventPurchased, Negate = true },
                    },
                });
            yield return ("Passes ending in 30 days", "Pass holders whose pass runs out within the next 30 days. Time a renewal offer off this.",
                new AudienceDefinition { Base = AudienceBases.Everyone, Rules = { new AudienceRule { Kind = AudienceRuleKinds.PassExpiring, Days = 30 } } });
        }

        /// <summary>The UTC instant the tenant's current calendar day began (falls back to UTC).</summary>
        private async Task<DateTime> TodayStartUtc(Guid tenantId)
        {
            var tzName = (await _db.Query<string?>("SELECT timezone FROM tenant WHERE id = @tenantId", new { tenantId })).FirstOrDefault();
            var tz = TimeZoneInfo.Utc;
            if (!string.IsNullOrWhiteSpace(tzName))
            {
                try { tz = TimeZoneInfo.FindSystemTimeZoneById(tzName); } catch { /* unknown zone: UTC */ }
            }
            var localToday = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).Date;
            return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localToday, DateTimeKind.Unspecified), tz);
        }
    }
}
