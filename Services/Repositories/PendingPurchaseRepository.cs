using Services.Helpers.Interfaces;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class PendingPurchaseRepository : IPendingPurchaseRepository
    {
        private readonly IDbHelper _db;

        public PendingPurchaseRepository(IDbHelper db)
        {
            _db = db;
        }

        public async Task<List<PendingPaymentIntent>> ListStalePendingPaymentIntents(DateTime olderThanUtc, int take = 200)
        {
            // One PaymentIntent can fan out to rows in several tables (a cart with a ticket
            // plus a gate-fee extra, a bundled membership, etc.), so union the pending rows
            // across every checkout table the finalizer knows how to settle and group by PI.
            // Only rows with a PI set and older than the cutoff are considered — fresh carts
            // are left alone. Rentals key off the fee PI (the charge PI the finalizer matches
            // on); the separate deposit hold isn't status-gating. Gift cards ARE included: a missed
            // webhook otherwise leaves a paid card stuck 'pending' forever (unspendable + never
            // delivered), so the reconciler must activate it (or void it if the charge was abandoned).
            // Every direct-charge-capable table carries the connected-account snapshot (tickets,
            // extras, membership, season pass, shop sale/rental, concession, gift card) so the reconciler queries
            // Stripe on the right account. MAX() over the PI group surfaces the non-null account for a
            // direct PI, since everything bundled on the same PI shares that charge.
            const string sql = @"
                SELECT stripe_payment_intent_id AS PaymentIntentId, MIN(created_at) AS OldestCreatedAtUtc,
                       MAX(connected_account_id) AS ConnectedAccountId
                FROM (
                    SELECT stripe_payment_intent_id, created_at, stripe_connected_account_id AS connected_account_id FROM event_ticket_purchase
                        WHERE status = 'pending' AND stripe_payment_intent_id IS NOT NULL AND created_at < @cutoff
                    UNION ALL
                    SELECT stripe_payment_intent_id, created_at, stripe_connected_account_id FROM event_extra_purchase
                        WHERE status = 'pending' AND stripe_payment_intent_id IS NOT NULL AND created_at < @cutoff
                    UNION ALL
                    SELECT stripe_payment_intent_id, created_at, stripe_connected_account_id FROM membership_purchase
                        WHERE status = 'pending' AND stripe_payment_intent_id IS NOT NULL AND created_at < @cutoff
                    UNION ALL
                    SELECT stripe_payment_intent_id, created_at, stripe_connected_account_id FROM season_pass_purchase
                        WHERE status = 'pending' AND stripe_payment_intent_id IS NOT NULL AND created_at < @cutoff
                    UNION ALL
                    SELECT stripe_payment_intent_id, created_at, stripe_connected_account_id FROM shop_sale
                        WHERE status = 'pending' AND stripe_payment_intent_id IS NOT NULL AND created_at < @cutoff
                    UNION ALL
                    SELECT stripe_payment_intent_id, created_at, stripe_connected_account_id FROM shop_rental
                        WHERE status = 'pending' AND stripe_payment_intent_id IS NOT NULL AND created_at < @cutoff
                    UNION ALL
                    SELECT stripe_payment_intent_id, created_at, stripe_connected_account_id FROM concession_sale
                        WHERE status = 'pending' AND stripe_payment_intent_id IS NOT NULL AND created_at < @cutoff
                    UNION ALL
                    SELECT stripe_payment_intent_id, created_at, stripe_connected_account_id FROM gift_card
                        WHERE status = 'pending' AND stripe_payment_intent_id IS NOT NULL AND created_at < @cutoff
                ) q
                GROUP BY stripe_payment_intent_id
                ORDER BY MIN(created_at)
                LIMIT @take";
            var rows = await _db.Query<PendingPaymentIntent>(sql, new { cutoff = olderThanUtc, take });
            return rows.ToList();
        }

        public async Task<List<PaymentlessPendingPurchase>> ListStalePendingWithoutPaymentIntent(DateTime olderThanUtc, int take = 200)
        {
            // The PI-keyed union above requires stripe_payment_intent_id IS NOT NULL on every
            // branch, so a checkout that died BEFORE a PaymentIntent was stamped (PI creation
            // threw, or the request died mid-flight) is invisible to it and its rows sit
            // 'pending' forever, holding inventory. This is the complementary query: same
            // tables, IS NULL. Age alone is a safe signal here because counter cash sales flip
            // to 'paid' in the same request and every card/online flow stamps the PI seconds
            // after creating the rows; nothing legitimate is still PI-less hours later.
            // Package-composed ticket/rental rows are the one exception: they ride the
            // package_purchase's PI without one of their own for the whole checkout, so they
            // are excluded rather than swept out from under a live package payment.
            // Deliberately cross-tenant: this feeds the platform-wide reconciler worker (no
            // tenant context), and rows are addressed by globally-unique ids.
            // AppliedSeasonPassPurchaseId / CreditAppliedCents ride along because those debits
            // happen before PI creation and the sweep must hand them back.
            const string sql = @"
                SELECT * FROM (
                    SELECT 'event_ticket_purchase' AS TableName, t.id, t.tenant_id AS TenantId, t.created_at,
                           t.applied_season_pass_purchase_id AS AppliedSeasonPassPurchaseId, 0 AS CreditAppliedCents
                    FROM event_ticket_purchase t
                    WHERE t.status = 'pending' AND t.stripe_payment_intent_id IS NULL AND t.created_at < @cutoff
                      AND NOT EXISTS (SELECT 1 FROM package_purchase pp WHERE pp.event_ticket_purchase_id = t.id)
                    UNION ALL
                    SELECT 'event_extra_purchase', x.id, x.tenant_id, x.created_at, NULL, 0
                    FROM event_extra_purchase x
                    WHERE x.status = 'pending' AND x.stripe_payment_intent_id IS NULL AND x.created_at < @cutoff
                    UNION ALL
                    SELECT 'membership_purchase', m.id, m.tenant_id, m.created_at, NULL, 0
                    FROM membership_purchase m
                    WHERE m.status = 'pending' AND m.stripe_payment_intent_id IS NULL AND m.created_at < @cutoff
                    UNION ALL
                    SELECT 'season_pass_purchase', sp.id, sp.tenant_id, sp.created_at, NULL, 0
                    FROM season_pass_purchase sp
                    WHERE sp.status = 'pending' AND sp.stripe_payment_intent_id IS NULL AND sp.created_at < @cutoff
                    UNION ALL
                    SELECT 'shop_sale', s.id, s.tenant_id, s.created_at, NULL, s.credit_applied_cents
                    FROM shop_sale s
                    WHERE s.status = 'pending' AND s.stripe_payment_intent_id IS NULL AND s.created_at < @cutoff
                    UNION ALL
                    SELECT 'shop_rental', r.id, r.tenant_id, r.created_at, NULL, 0
                    FROM shop_rental r
                    WHERE r.status = 'pending' AND r.stripe_payment_intent_id IS NULL AND r.created_at < @cutoff
                      AND NOT EXISTS (SELECT 1 FROM package_purchase pp WHERE pp.shop_rental_id = r.id)
                    UNION ALL
                    SELECT 'concession_sale', c.id, c.tenant_id, c.created_at, NULL, c.credit_applied_cents
                    FROM concession_sale c
                    WHERE c.status = 'pending' AND c.stripe_payment_intent_id IS NULL AND c.created_at < @cutoff
                    UNION ALL
                    SELECT 'gift_card', g.id, g.tenant_id, g.created_at, NULL, 0
                    FROM gift_card g
                    WHERE g.status = 'pending' AND g.stripe_payment_intent_id IS NULL AND g.created_at < @cutoff
                ) q
                ORDER BY created_at
                LIMIT @take";
            var rows = await _db.Query<PaymentlessPendingPurchase>(sql, new { cutoff = olderThanUtc, take });
            return rows.ToList();
        }

        public async Task<List<Guid>> MarkAbandonedWithoutPaymentIntent(string tableName, IReadOnlyList<Guid> ids)
        {
            if (ids.Count == 0) return new List<Guid>();

            // The table name selects one of a fixed set of statements (never interpolated), and
            // every UPDATE re-asserts pending + PI-less so a checkout that stamped its PI after
            // the listing keeps its rows for the PI reconciliation path instead. A gift card has
            // no 'abandoned' in its status vocabulary; 'void' is its dead state (what the
            // finalizer writes when a card purchase dies), and it fails equally safe: spend and
            // delivery both gate on status = 'active'.
            var sql = tableName switch
            {
                "event_ticket_purchase" => @"
                    UPDATE event_ticket_purchase SET status = 'abandoned', updated_at = now()
                    WHERE id = ANY(@ids) AND status = 'pending' AND stripe_payment_intent_id IS NULL
                    RETURNING id",
                "event_extra_purchase" => @"
                    UPDATE event_extra_purchase SET status = 'abandoned'
                    WHERE id = ANY(@ids) AND status = 'pending' AND stripe_payment_intent_id IS NULL
                    RETURNING id",
                "membership_purchase" => @"
                    UPDATE membership_purchase SET status = 'abandoned'
                    WHERE id = ANY(@ids) AND status = 'pending' AND stripe_payment_intent_id IS NULL
                    RETURNING id",
                "season_pass_purchase" => @"
                    UPDATE season_pass_purchase SET status = 'abandoned', updated_at = now()
                    WHERE id = ANY(@ids) AND status = 'pending' AND stripe_payment_intent_id IS NULL
                    RETURNING id",
                "shop_sale" => @"
                    UPDATE shop_sale SET status = 'abandoned', updated_at = now()
                    WHERE id = ANY(@ids) AND status = 'pending' AND stripe_payment_intent_id IS NULL
                    RETURNING id",
                "shop_rental" => @"
                    UPDATE shop_rental SET status = 'abandoned', updated_at = now()
                    WHERE id = ANY(@ids) AND status = 'pending' AND stripe_payment_intent_id IS NULL
                    RETURNING id",
                "concession_sale" => @"
                    UPDATE concession_sale SET status = 'abandoned'
                    WHERE id = ANY(@ids) AND status = 'pending' AND stripe_payment_intent_id IS NULL
                    RETURNING id",
                "gift_card" => @"
                    UPDATE gift_card SET status = 'void'
                    WHERE id = ANY(@ids) AND status = 'pending' AND stripe_payment_intent_id IS NULL
                    RETURNING id",
                _ => throw new ArgumentException($"Unknown paymentless purchase table '{tableName}'.", nameof(tableName)),
            };
            var flipped = await _db.Query<Guid>(sql, new { ids = ids.ToArray() });
            return flipped.ToList();
        }
    }
}
