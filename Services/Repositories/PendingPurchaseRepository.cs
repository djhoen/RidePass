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
            const string sql = @"
                SELECT stripe_payment_intent_id AS PaymentIntentId, MIN(created_at) AS OldestCreatedAtUtc
                FROM (
                    SELECT stripe_payment_intent_id, created_at FROM event_ticket_purchase
                        WHERE status = 'pending' AND stripe_payment_intent_id IS NOT NULL AND created_at < @cutoff
                    UNION ALL
                    SELECT stripe_payment_intent_id, created_at FROM event_extra_purchase
                        WHERE status = 'pending' AND stripe_payment_intent_id IS NOT NULL AND created_at < @cutoff
                    UNION ALL
                    SELECT stripe_payment_intent_id, created_at FROM membership_purchase
                        WHERE status = 'pending' AND stripe_payment_intent_id IS NOT NULL AND created_at < @cutoff
                    UNION ALL
                    SELECT stripe_payment_intent_id, created_at FROM season_pass_purchase
                        WHERE status = 'pending' AND stripe_payment_intent_id IS NOT NULL AND created_at < @cutoff
                    UNION ALL
                    SELECT rental_pi_id, created_at FROM rental_purchase
                        WHERE status = 'pending' AND rental_pi_id IS NOT NULL AND created_at < @cutoff
                ) q
                GROUP BY stripe_payment_intent_id
                ORDER BY MIN(created_at)
                LIMIT @take";
            var rows = await _db.Query<PendingPaymentIntent>(sql, new { cutoff = olderThanUtc, take });
            return rows.ToList();
        }
    }
}
