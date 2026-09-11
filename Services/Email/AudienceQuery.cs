using Dapper;
using Services.Repositories.Data.NewsletterData;

namespace Services.Email
{
    /// <summary>
    /// Turns an <see cref="AudienceDefinition"/> into one SQL statement that lists its people
    /// (email, name, user id) for a tenant. Every subquery carries tenant_id, and a rule's ids
    /// are only ever compared inside those tenant-scoped subqueries, so a foreign id matches
    /// nobody rather than someone else's riders. No user input reaches the SQL text: rule values
    /// travel as parameters (arrays for lists).
    /// </summary>
    public static class AudienceQuery
    {
        public const string TenantParam = "tenantId";

        private const string SubscribersSql = @"
            SELECT lower(s.email) AS email, s.name AS name, NULL::uuid AS user_id
            FROM newsletter_subscriber s
            WHERE s.tenant_id = @tenantId AND s.unsubscribed_at IS NULL";

        private const string CustomersSql = @"
            SELECT lower(p.purchaser_email) AS email, p.purchaser_name AS name, p.purchaser_user_id AS user_id
            FROM event_ticket_purchase p
            WHERE p.tenant_id = @tenantId AND p.status IN ('paid', 'redeemed')
            UNION ALL
            SELECT lower(sp.purchaser_email), sp.purchaser_name, sp.purchaser_user_id
            FROM season_pass_purchase sp
            WHERE sp.tenant_id = @tenantId AND sp.status = 'paid'";

        /// <summary>Started a checkout that never completed: the people an abandoned-cart audience is about.</summary>
        private const string UnfinishedSql = @"
            SELECT lower(p.purchaser_email) AS email, p.purchaser_name AS name, p.purchaser_user_id AS user_id
            FROM event_ticket_purchase p
            WHERE p.tenant_id = @tenantId AND p.status IN ('pending', 'abandoned', 'failed')
            UNION ALL
            SELECT lower(sp.purchaser_email), sp.purchaser_name, sp.purchaser_user_id
            FROM season_pass_purchase sp
            WHERE sp.tenant_id = @tenantId AND sp.status IN ('pending', 'abandoned', 'failed')";

        /// <summary>
        /// The statement and its parameters. <paramref name="todayStartUtc"/> is the instant the
        /// tenant's current day began, so "yesterday's abandoned carts" is yesterday on their
        /// calendar. Columns: email, name, userid.
        /// </summary>
        public static (string Sql, DynamicParameters Params) Build(Guid tenantId, AudienceDefinition def, DateTime todayStartUtc)
        {
            var p = new DynamicParameters();
            p.Add(TenantParam, tenantId);

            var baseSql = def.Base switch
            {
                AudienceBases.Subscribers => SubscribersSql,
                AudienceBases.Customers => CustomersSql,
                _ => SubscribersSql + "\nUNION ALL\n" + CustomersSql + "\nUNION ALL\n" + UnfinishedSql,
            };

            var predicates = new List<string>();
            for (var i = 0; i < def.Rules.Count; i++)
            {
                var rule = def.Rules[i];
                var pred = Predicate(rule, i, p, todayStartUtc);
                if (pred is null) continue;
                // COALESCE so NOT of an address rule with no address on file excludes cleanly
                // instead of evaluating to NULL and vanishing from both sides.
                pred = $"COALESCE(({pred}), FALSE)";
                predicates.Add(rule.Negate ? $"NOT {pred}" : pred);
            }
            var joiner = string.Equals(def.Match, "any", StringComparison.OrdinalIgnoreCase) ? " OR " : " AND ";
            var where = predicates.Count == 0 ? "TRUE" : string.Join(joiner, predicates);

            // One row per inbox, preferring the row that knows a name and an account.
            var sql = $@"
                WITH people AS (
                    {baseSql}
                ),
                person AS (
                    SELECT DISTINCT ON (p.email) p.email, p.name, p.user_id
                    FROM people p
                    WHERE p.email IS NOT NULL AND p.email <> ''
                    ORDER BY p.email, (p.user_id IS NULL), (NULLIF(p.name, '') IS NULL)
                )
                SELECT ps.email AS Email, ps.name AS Name, ps.user_id AS UserId, u.phone AS Phone
                FROM person ps
                LEFT JOIN LATERAL (
                    SELECT u.postal_code, u.state, u.city, u.phone
                    FROM users u
                    WHERE lower(u.email) = ps.email AND (u.tenant_id = @tenantId OR u.tenant_id IS NULL)
                    ORDER BY (u.tenant_id = @tenantId) DESC, u.created_at DESC
                    LIMIT 1
                ) u ON TRUE
                WHERE {where}
                ORDER BY ps.email";
            return (sql, p);
        }

        private static string? Predicate(AudienceRule rule, int i, DynamicParameters p, DateTime todayStartUtc)
        {
            var n = $"r{i}_";
            switch (rule.Kind)
            {
                case AudienceRuleKinds.EventPurchased:
                case AudienceRuleKinds.EventTypePurchased:
                {
                    var byType = rule.Kind == AudienceRuleKinds.EventTypePurchased;
                    p.Add(n + "any", rule.Ids.Count == 0);
                    p.Add(n + "ids", rule.Ids.ToArray());
                    p.Add(n + "from", rule.FromUtc);
                    p.Add(n + "to", rule.ToUtc);
                    var idClause = byType ? $"e.event_type_id = ANY(@{n}ids)" : $"e.id = ANY(@{n}ids)";
                    return $@"EXISTS (
                        SELECT 1 FROM event_ticket_purchase x
                        JOIN event_ticket_tier t ON t.id = x.tier_id
                        JOIN event e ON e.id = t.event_id AND e.tenant_id = x.tenant_id
                        WHERE x.tenant_id = @tenantId AND lower(x.purchaser_email) = ps.email
                          AND x.status IN ('paid', 'redeemed')
                          AND (@{n}any OR {idClause})
                          AND (CAST(@{n}from AS timestamptz) IS NULL OR e.starts_at >= CAST(@{n}from AS timestamptz))
                          AND (CAST(@{n}to   AS timestamptz) IS NULL OR e.starts_at <  CAST(@{n}to   AS timestamptz)))";
                }
                case AudienceRuleKinds.PassHolder:
                    p.Add(n + "any", rule.Ids.Count == 0);
                    p.Add(n + "ids", rule.Ids.ToArray());
                    p.Add(n + "active", rule.ActiveOnly);
                    return $@"EXISTS (
                        SELECT 1 FROM season_pass_purchase sp
                        WHERE sp.tenant_id = @tenantId AND lower(sp.purchaser_email) = ps.email
                          AND sp.status = 'paid'
                          AND (@{n}any OR sp.product_id = ANY(@{n}ids))
                          AND (NOT @{n}active OR sp.valid_to_date >= CURRENT_DATE))";
                case AudienceRuleKinds.PassExpiring:
                    p.Add(n + "days", Math.Clamp(rule.Days ?? 30, 0, 3650));
                    return $@"EXISTS (
                        SELECT 1 FROM season_pass_purchase sp
                        WHERE sp.tenant_id = @tenantId AND lower(sp.purchaser_email) = ps.email
                          AND sp.status = 'paid'
                          AND sp.valid_to_date >= CURRENT_DATE
                          AND sp.valid_to_date <= CURRENT_DATE + @{n}days)";
                case AudienceRuleKinds.AbandonedCart:
                {
                    // The last N whole days on the tenant's calendar, not today: a checkout started
                    // an hour ago may still complete. A later paid purchase means they came back.
                    var days = Math.Clamp(rule.Days ?? 1, 1, 365);
                    p.Add(n + "from", todayStartUtc.AddDays(-days));
                    p.Add(n + "to", todayStartUtc);
                    // Written as a non-correlated IN so Postgres builds the (small) set of
                    // unfinished checkouts once and hashes it, rather than re-scanning both
                    // purchase tables for each of tens of thousands of people.
                    return $@"ps.email IN (
                        SELECT lower(c.purchaser_email)
                        FROM (
                            SELECT x.purchaser_email, x.created_at FROM event_ticket_purchase x
                            WHERE x.tenant_id = @tenantId AND x.status IN ('pending', 'abandoned', 'failed')
                              AND x.created_at >= @{n}from AND x.created_at < @{n}to
                            UNION ALL
                            SELECT sp.purchaser_email, sp.created_at FROM season_pass_purchase sp
                            WHERE sp.tenant_id = @tenantId AND sp.status IN ('pending', 'abandoned', 'failed')
                              AND sp.created_at >= @{n}from AND sp.created_at < @{n}to
                        ) c
                        WHERE NOT EXISTS (
                                SELECT 1 FROM event_ticket_purchase y
                                WHERE y.tenant_id = @tenantId AND lower(y.purchaser_email) = lower(c.purchaser_email)
                                  AND y.status IN ('paid', 'redeemed') AND y.created_at >= c.created_at)
                          AND NOT EXISTS (
                                SELECT 1 FROM season_pass_purchase z
                                WHERE z.tenant_id = @tenantId AND lower(z.purchaser_email) = lower(c.purchaser_email)
                                  AND z.status = 'paid' AND z.created_at >= c.created_at))";
                }
                case AudienceRuleKinds.PostalCode:
                {
                    // "03053" matches 03053 and 03053-1234; "030" matches every ZIP starting 030;
                    // "*" is a wildcard anywhere. Spaces are ignored on both sides.
                    var patterns = rule.Values
                        .Select(v => new string((v ?? "").Where(ch => !char.IsWhiteSpace(ch)).ToArray()))
                        .Where(v => v.Length > 0)
                        .Select(v => v.Contains('*') ? v.Replace('*', '%') : v + "%")
                        .ToArray();
                    if (patterns.Length == 0) return null;
                    p.Add(n + "vals", patterns);
                    return $@"(u.postal_code IS NOT NULL AND regexp_replace(u.postal_code, '\s', '', 'g') ILIKE ANY(@{n}vals))";
                }
                case AudienceRuleKinds.State:
                {
                    var vals = rule.Values.Select(v => (v ?? "").Trim().ToUpperInvariant()).Where(v => v.Length > 0).ToArray();
                    if (vals.Length == 0) return null;
                    p.Add(n + "vals", vals);
                    return $"(u.state IS NOT NULL AND upper(trim(u.state)) = ANY(@{n}vals))";
                }
                case AudienceRuleKinds.City:
                {
                    var vals = rule.Values.Select(v => (v ?? "").Trim().ToLowerInvariant()).Where(v => v.Length > 0).ToArray();
                    if (vals.Length == 0) return null;
                    p.Add(n + "vals", vals);
                    return $"(u.city IS NOT NULL AND lower(trim(u.city)) = ANY(@{n}vals))";
                }
                default:
                    return null;
            }
        }

        /// <summary>The tenant-facing sentence for one rule, for the list and the campaign label.</summary>
        public static string Describe(AudienceRule r, Func<Guid, string?> nameOf)
        {
            string names(IEnumerable<Guid> ids) => string.Join(", ", ids.Select(id => nameOf(id) ?? "a removed item"));
            var not = r.Negate;
            return r.Kind switch
            {
                AudienceRuleKinds.EventPurchased => r.Ids.Count == 0
                    ? (not ? "have not bought an event ticket" : "bought an event ticket")
                    : (not ? "did not buy a ticket to " : "bought a ticket to ") + names(r.Ids),
                AudienceRuleKinds.EventTypePurchased => (not ? "did not buy a ticket to any " : "bought a ticket to any ") + names(r.Ids),
                AudienceRuleKinds.PassHolder => r.Ids.Count == 0
                    ? (not ? "do not hold a pass" : (r.ActiveOnly ? "hold a current pass" : "have bought a pass"))
                    : (not ? "do not hold " : (r.ActiveOnly ? "hold a current " : "have bought ")) + names(r.Ids),
                AudienceRuleKinds.PassExpiring => (not ? "whose pass does not end" : "whose pass ends") + $" within {r.Days ?? 30} days",
                AudienceRuleKinds.AbandonedCart => (not ? "did not leave" : "left") + $" a checkout unfinished in the last {r.Days ?? 1} day{((r.Days ?? 1) == 1 ? "" : "s")}",
                AudienceRuleKinds.PostalCode => (not ? "ZIP is not " : "ZIP is ") + string.Join(", ", r.Values),
                AudienceRuleKinds.State => (not ? "state is not " : "state is ") + string.Join(", ", r.Values),
                AudienceRuleKinds.City => (not ? "city is not " : "city is ") + string.Join(", ", r.Values),
                _ => r.Kind,
            };
        }

        public static string DescribeBase(string b) => b switch
        {
            AudienceBases.Subscribers => "Newsletter subscribers",
            AudienceBases.Customers => "Customers",
            _ => "Everyone",
        };
    }
}
