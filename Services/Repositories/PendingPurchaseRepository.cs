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
            // are left alone. Rentals key off rental_pi_id (the charge PI the finalizer matches
            // on); the separate deposit hold isn't status-gating. Gift cards are intentionally
            // excluded: they hold no inventory and have their own delivery worker.
            // Every direct-charge-capable table carries the connected-account snapshot (tickets,
            // extras, membership, season pass, rental, concession) so the reconciler queries Stripe on
            // the right account. MAX() over the PI group surfaces the non-null account for a direct PI,
            // since everything bundled on the same PI shares that charge.
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
                    SELECT rental_pi_id, created_at, stripe_connected_account_id FROM rental_purchase
                        WHERE status = 'pending' AND rental_pi_id IS NOT NULL AND created_at < @cutoff
                    UNION ALL
                    SELECT stripe_payment_intent_id, created_at, stripe_connected_account_id FROM concession_sale
                        WHERE status = 'pending' AND stripe_payment_intent_id IS NOT NULL AND created_at < @cutoff
                ) q
                GROUP BY stripe_payment_intent_id
                ORDER BY MIN(created_at)
                LIMIT @take";
            var rows = await _db.Query<PendingPaymentIntent>(sql, new { cutoff = olderThanUtc, take });
            return rows.ToList();
        }
    }
}
