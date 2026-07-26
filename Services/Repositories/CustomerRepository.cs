using Services.Helpers.Interfaces;
using Services.Repositories.Data.CustomerData;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Data.UserData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    // The "customer" view is derived — there's no Customer table. A person becomes
    // a customer of a tenant the moment they make any paid purchase or sign a
    // waiver at that tenant. ListForTenant unions activity across three purchase
    // tables + RiderWaiverSignature, deduplicates by user_id, then re-joins to
    // the User table for profile fields.
    //
    // Critical scoping rule: every query filters by tenant_id on the activity
    // tables. The user's User.tenant_id (where they registered) is irrelevant —
    // a user can be a customer of multiple tenants.
    public class CustomerRepository : ICustomerRepository
    {
        private readonly IDbHelper _db;

        public CustomerRepository(IDbHelper db) => _db = db;

        // Common subquery: every (user_id, activity_at, amount_cents, is_paid) row
        // for this tenant across all three purchase tables. A SETTLED payment is the bar for
        // counting toward purchase totals, which for a ticket means 'paid' OR 'redeemed': redeemed
        // is where a paid ticket LANDS once it is scanned at the gate, not a separate kind of sale.
        // Counting only 'paid' hid the majority of real spend, since most tickets get scanned. Un-paid
        // rows still mark the
        // user as a customer (so support can see them in the list) - including
        // 'pending' and 'failed', both of which are a real interaction (a payment
        // attempt happened). The one exception is 'abandoned': that status means
        // the reconciler killed a checkout that never had a completed payment
        // attempt at all, so excluding it (and only it) keeps an empty cart from
        // minting a customer row or stamping LastActivityAt with the moment
        // someone closed the tab.
        private const string ActivityCte = @"
            WITH activity AS (
                SELECT purchaser_user_id AS user_id,
                       created_at AS activity_at,
                       amount_cents,
                       (status IN ('paid', 'redeemed'))::int AS is_paid
                FROM event_ticket_purchase
                WHERE tenant_id = @tenantId AND purchaser_user_id IS NOT NULL
                  AND status <> 'abandoned'
                UNION ALL
                SELECT purchaser_user_id AS user_id,
                       created_at AS activity_at,
                       amount_cents,
                       -- No 'redeemed' here on purpose: a season pass never enters that status
                       -- (its vocabulary is pending/paid/failed/cancelled/refunded/upgraded).
                       (status = 'paid')::int AS is_paid
                FROM season_pass_purchase
                WHERE tenant_id = @tenantId AND purchaser_user_id IS NOT NULL
                  AND status <> 'abandoned'
                UNION ALL
                SELECT user_id,
                       signed_at AS activity_at,
                       0 AS amount_cents,
                       0 AS is_paid
                FROM rider_waiver_signature
                WHERE tenant_id = @tenantId
            ),
            waivers AS (
                SELECT DISTINCT user_id
                FROM rider_waiver_signature
                WHERE tenant_id = @tenantId
            )";

        public async Task<List<CustomerSummary>> ListForTenant(Guid tenantId, string? search, int limit, int offset)
        {
            var sql = ActivityCte + @"
                SELECT u.id AS UserId,
                       u.email AS Email,
                       u.first_name AS FirstName,
                       u.last_name AS LastName,
                       u.birthdate AS Birthdate,
                       MAX(a.activity_at) AS LastActivityAt,
                       SUM(a.is_paid)::int AS TotalPurchases,
                       SUM(CASE WHEN a.is_paid = 1 THEN a.amount_cents ELSE 0 END)::int AS TotalSpentCents,
                       (w.user_id IS NOT NULL) AS HasWaiverSigned
                FROM activity a
                JOIN users u ON u.id = a.user_id
                LEFT JOIN waivers w ON w.user_id = u.id
                WHERE (@search IS NULL OR @search = ''
                       OR u.first_name ILIKE '%' || @search || '%'
                       OR u.last_name  ILIKE '%' || @search || '%'
                       OR (u.first_name || ' ' || u.last_name) ILIKE '%' || @search || '%'
                       OR u.email      ILIKE '%' || @search || '%')
                GROUP BY u.id, u.email, u.first_name, u.last_name, u.birthdate, w.user_id
                ORDER BY MAX(a.activity_at) DESC NULLS LAST
                LIMIT @limit OFFSET @offset";

            var rows = await _db.Query<CustomerSummary>(sql, new { tenantId, search, limit, offset });
            return rows.ToList();
        }

        public async Task<int> CountForTenant(Guid tenantId, string? search)
        {
            var sql = ActivityCte + @"
                SELECT COUNT(DISTINCT u.id)
                FROM activity a
                JOIN users u ON u.id = a.user_id
                WHERE (@search IS NULL OR @search = ''
                       OR u.first_name ILIKE '%' || @search || '%'
                       OR u.last_name  ILIKE '%' || @search || '%'
                       OR (u.first_name || ' ' || u.last_name) ILIKE '%' || @search || '%'
                       OR u.email      ILIKE '%' || @search || '%')";
            var rows = await _db.Query<int>(sql, new { tenantId, search });
            return rows.FirstOrDefault();
        }

        public async Task<CustomerDetail?> GetDetail(Guid userId, Guid tenantId)
        {
            // Gate: the user must have at least one activity row at this tenant.
            // Otherwise we 404 — prevents tenants from poking around in users they
            // have no relationship with.
            const string gateSql = @"
                SELECT 1 FROM (
                    SELECT 1 FROM event_ticket_purchase WHERE tenant_id = @tenantId AND purchaser_user_id = @userId
                    UNION ALL
                    SELECT 1 FROM season_pass_purchase WHERE tenant_id = @tenantId AND purchaser_user_id = @userId
                    UNION ALL
                    SELECT 1 FROM rider_waiver_signature WHERE tenant_id = @tenantId AND user_id = @userId
                ) g LIMIT 1";
            var hasActivity = (await _db.Query<int>(gateSql, new { tenantId, userId })).Any();
            if (!hasActivity) return null;

            // User profile.
            const string userSql = @"
                SELECT id, tenant_id AS TenantId, email, first_name AS FirstName, last_name AS LastName,
                       role, status, phone, birthdate, emergency_contact_name AS EmergencyContactName,
                       emergency_contact_phone AS EmergencyContactPhone
                FROM users WHERE id = @userId LIMIT 1";
            var user = (await _db.Query<User>(userSql, new { userId })).FirstOrDefault();
            if (user == null) return null;

            const string eventTicketSql = @"
                SELECT id, tenant_id AS TenantId, tier_id AS TierId, purchaser_user_id AS PurchaserUserId,
                       amount_cents AS AmountCents, status, purchaser_email AS PurchaserEmail,
                       purchaser_name AS PurchaserName, created_at AS CreatedAt, updated_at AS UpdatedAt
                FROM event_ticket_purchase
                WHERE tenant_id = @tenantId AND purchaser_user_id = @userId
                ORDER BY created_at DESC";
            var eventTickets = (await _db.Query<EventTicketPurchase>(eventTicketSql, new { tenantId, userId })).ToList();

            const string seasonPassSql = @"
                SELECT id, tenant_id AS TenantId, purchaser_user_id AS PurchaserUserId, product_id AS ProductId,
                       waiver_signature_id AS WaiverSignatureId, amount_cents AS AmountCents,
                       service_charge_cents AS ServiceChargeCents, payment_method AS PaymentMethod,
                       status, purchaser_email AS PurchaserEmail, purchaser_name AS PurchaserName,
                       redemption_token AS RedemptionToken, valid_from_date AS ValidFromDate,
                       valid_to_date AS ValidToDate, credits_remaining AS CreditsRemaining,
                       created_at AS CreatedAt, updated_at AS UpdatedAt
                FROM season_pass_purchase
                WHERE tenant_id = @tenantId AND purchaser_user_id = @userId
                ORDER BY created_at DESC";
            var seasonPasses = (await _db.Query<SeasonPassPurchase>(seasonPassSql, new { tenantId, userId })).ToList();

            const string waiverSql = @"
                SELECT s.id, s.tenant_id AS TenantId, s.user_id AS UserId, s.waiver_id AS WaiverId,
                       s.signed_at AS SignedAt, s.ip_address AS IpAddress,
                       s.signature_data_url AS SignatureDataUrl, s.signed_by_parent AS SignedByParent,
                       s.parent_name AS ParentName, s.parent_phone AS ParentPhone,
                       w.title AS WaiverTitle, w.version AS WaiverVersion
                FROM rider_waiver_signature s
                JOIN tenant_waiver w ON w.id = s.waiver_id
                WHERE s.tenant_id = @tenantId AND s.user_id = @userId
                ORDER BY s.signed_at DESC";
            var waivers = (await _db.Query<RiderWaiverSignatureWithWaiver>(waiverSql, new { tenantId, userId })).ToList();

            // Totals from the SAME activity CTE the list summary uses, scoped to this user,
            // so the detail page can't disagree with the list.
            const string totalsSql = ActivityCte + @"
                SELECT COALESCE(SUM(is_paid), 0)::int AS TotalPurchases,
                       COALESCE(SUM(CASE WHEN is_paid = 1 THEN amount_cents ELSE 0 END), 0)::int AS TotalSpentCents
                FROM activity WHERE user_id = @userId";
            var totals = (await _db.Query<TotalsRow>(totalsSql, new { tenantId, userId })).FirstOrDefault();

            return new CustomerDetail
            {
                User = user,
                EventTickets = eventTickets,
                SeasonPasses = seasonPasses,
                WaiverSignatures = waivers,
                TotalPurchases = totals?.TotalPurchases ?? 0,
                TotalSpentCents = totals?.TotalSpentCents ?? 0,
            };
        }

        private sealed class TotalsRow
        {
            public int TotalPurchases { get; set; }
            public int TotalSpentCents { get; set; }
        }

        public async Task<List<TopRiderEntry>> GetTopRiders(Guid tenantId, string metric, string period, int limit)
        {
            // Anchor the period to the current calendar month or year. We could
            // accept arbitrary date ranges later if reps want it.
            var now = DateTime.UtcNow;
            DateTime since = period == "year"
                ? new DateTime(now.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                : new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            // Riders only — must have a waiver signed at this tenant. Otherwise
            // it's just a frequent buyer, not a "rider" per the widget's framing.
            // Days = number of paid passes/tickets in the period (NOT line-item
            // quantity — close enough for "frequency" without double-counting).
            var orderBy = metric == "spent" ? "spent_cents" : "days";

            var sql = $@"
                WITH paid_activity AS (
                    SELECT purchaser_user_id AS user_id, amount_cents
                    FROM event_ticket_purchase
                    WHERE tenant_id = @tenantId AND purchaser_user_id IS NOT NULL
                      AND status IN ('paid', 'redeemed') AND created_at >= @since
                    UNION ALL
                    SELECT purchaser_user_id AS user_id, amount_cents
                    FROM season_pass_purchase
                    WHERE tenant_id = @tenantId AND purchaser_user_id IS NOT NULL
                      AND status = 'paid' AND created_at >= @since
                ),
                riders AS (
                    SELECT DISTINCT user_id
                    FROM rider_waiver_signature
                    WHERE tenant_id = @tenantId
                )
                SELECT u.id AS UserId,
                       u.first_name AS FirstName,
                       u.last_name AS LastName,
                       u.email AS Email,
                       COUNT(*)::int AS days,
                       SUM(a.amount_cents)::int AS spent_cents
                FROM paid_activity a
                JOIN riders r ON r.user_id = a.user_id
                JOIN users u ON u.id = a.user_id
                GROUP BY u.id, u.first_name, u.last_name, u.email
                ORDER BY {orderBy} DESC
                LIMIT @limit";

            var rows = await _db.Query<TopRiderEntry>(sql, new { tenantId, since, limit });
            return rows.ToList();
        }
    }
}
