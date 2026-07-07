using Services.Helpers.Interfaces;
using Services.Repositories.Data.GiftCardData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class GiftCardRepository : IGiftCardRepository
    {
        private const string Columns = @"
            id, tenant_id AS TenantId, code,
            initial_amount_cents AS InitialAmountCents, balance_cents AS BalanceCents,
            buyer_user_id AS BuyerUserId, buyer_name AS BuyerName, buyer_email AS BuyerEmail,
            recipient_name AS RecipientName, recipient_email AS RecipientEmail,
            personal_note AS PersonalNote,
            delivery_status AS DeliveryStatus,
            scheduled_delivery_at_utc AS ScheduledDeliveryAtUtc,
            delivered_at_utc AS DeliveredAtUtc,
            status,
            stripe_payment_intent_id AS StripePaymentIntentId,
            stripe_connected_account_id AS StripeConnectedAccountId,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        private readonly IDbHelper _db;
        public GiftCardRepository(IDbHelper db) => _db = db;

        public async Task<Guid> Create(GiftCard c)
        {
            const string sql = @"
                INSERT INTO gift_card
                    (tenant_id, code, initial_amount_cents, balance_cents,
                     buyer_user_id, buyer_name, buyer_email,
                     recipient_name, recipient_email, personal_note,
                     delivery_status, scheduled_delivery_at_utc, delivered_at_utc,
                     status, stripe_payment_intent_id)
                VALUES
                    (@TenantId, @Code, @InitialAmountCents, @BalanceCents,
                     @BuyerUserId, @BuyerName, @BuyerEmail,
                     @RecipientName, @RecipientEmail, @PersonalNote,
                     @DeliveryStatus, @ScheduledDeliveryAtUtc, @DeliveredAtUtc,
                     @Status, @StripePaymentIntentId)
                RETURNING id";
            return (await _db.Query<Guid>(sql, c)).First();
        }

        public async Task<GiftCard?> GetById(Guid id, Guid tenantId)
        {
            var sql = $"SELECT {Columns} FROM gift_card WHERE id = @id AND tenant_id = @tenantId LIMIT 1";
            return (await _db.Query<GiftCard>(sql, new { id, tenantId })).FirstOrDefault();
        }

        public async Task<GiftCard?> GetByCode(Guid tenantId, string code)
        {
            var sql = $"SELECT {Columns} FROM gift_card WHERE tenant_id = @tenantId AND lower(code) = lower(@code) LIMIT 1";
            return (await _db.Query<GiftCard>(sql, new { tenantId, code })).FirstOrDefault();
        }

        public async Task<GiftCard?> GetByPaymentIntentId(string paymentIntentId)
        {
            var sql = $"SELECT {Columns} FROM gift_card WHERE stripe_payment_intent_id = @paymentIntentId LIMIT 1";
            return (await _db.Query<GiftCard>(sql, new { paymentIntentId })).FirstOrDefault();
        }

        public async Task SetStripePaymentIntentId(Guid id, string paymentIntentId, string? connectedAccountId = null)
        {
            // Snapshot the connected account alongside the PI so the reconciler can query Stripe on the
            // right account for direct-charge tenants (NULL leaves it as a platform charge).
            const string sql = @"UPDATE gift_card
                SET stripe_payment_intent_id = @paymentIntentId,
                    stripe_connected_account_id = @connectedAccountId
                WHERE id = @id";
            await _db.Execute(sql, new { id, paymentIntentId, connectedAccountId });
        }

        public async Task<bool> ApplyToBalance(Guid id, int amountCents)
        {
            // Atomic CONDITIONAL decrement + status flip. The `balance_cents >= @amountCents`
            // guard means two concurrent checkouts on the same card can't both spend it: the
            // loser's UPDATE matches no row and returns false, so the caller can reject it
            // instead of driving the balance negative (double-spend). CASE handles the depleted
            // transition without a follow-up UPDATE; refund flow uses a separate path.
            const string sql = @"
                UPDATE gift_card
                SET balance_cents = balance_cents - @amountCents,
                    status = CASE WHEN balance_cents - @amountCents <= 0 THEN 'depleted' ELSE status END
                WHERE id = @id AND balance_cents >= @amountCents";
            return await _db.Execute(sql, new { id, amountCents }) > 0;
        }

        public async Task RestoreBalance(Guid id, int amountCents)
        {
            // Atomic increment + un-deplete. Mirror of ApplyToBalance for the reverse direction.
            const string sql = @"
                UPDATE gift_card
                SET balance_cents = balance_cents + @amountCents,
                    status = CASE WHEN status = 'depleted' AND balance_cents + @amountCents > 0 THEN 'active' ELSE status END
                WHERE id = @id";
            await _db.Execute(sql, new { id, amountCents });
        }

        public async Task<List<GiftCardRedemption>> DeleteRedemptionsBySource(string sourceKind, IReadOnlyList<Guid> sourceIds)
        {
            if (sourceIds.Count == 0) return new List<GiftCardRedemption>();
            const string sql = @"
                DELETE FROM gift_card_redemption
                WHERE source_kind = @sourceKind AND source_id = ANY(@sourceIds)
                RETURNING id, gift_card_id AS GiftCardId, tenant_id AS TenantId, user_id AS UserId,
                          source_kind AS SourceKind, source_id AS SourceId, amount_cents AS AmountCents,
                          redeemed_at AS RedeemedAt";
            return (await _db.Query<GiftCardRedemption>(sql, new { sourceKind, sourceIds = sourceIds.ToArray() })).ToList();
        }

        // Returns true only when this call actually flipped pending → active, so the caller can send
        // the one-time delivery email exactly once even if the success event is processed more than
        // once (a late webhook racing the reconciler that already finalized the card).
        public async Task<bool> Activate(Guid id)
        {
            const string sql = "UPDATE gift_card SET status = 'active' WHERE id = @id AND status = 'pending'";
            return await _db.Execute(sql, new { id }) > 0;
        }

        public async Task Void(Guid id)
        {
            const string sql = "UPDATE gift_card SET status = 'void' WHERE id = @id AND status = 'pending'";
            await _db.Execute(sql, new { id });
        }

        public async Task MarkDelivered(Guid id)
        {
            const string sql = @"
                UPDATE gift_card
                SET delivery_status = 'delivered',
                    delivered_at_utc = now()
                WHERE id = @id";
            await _db.Execute(sql, new { id });
        }

        public async Task<List<GiftCard>> ListPendingDelivery(DateTime cutoffUtc, int take)
        {
            // null scheduled_delivery_at_utc means "send immediately on payment"; the
            // partial index `idx_gift_card_pending_delivery` covers both null + future-dated.
            var sql = $@"
                SELECT {Columns}
                FROM gift_card
                WHERE delivery_status = 'pending'
                  AND status = 'active'
                  AND (scheduled_delivery_at_utc IS NULL OR scheduled_delivery_at_utc <= @cutoffUtc)
                ORDER BY created_at
                LIMIT @take";
            return (await _db.Query<GiftCard>(sql, new { cutoffUtc, take })).ToList();
        }

        public async Task<int> CountRedemptions(Guid giftCardId)
        {
            const string sql = "SELECT COUNT(*) FROM gift_card_redemption WHERE gift_card_id = @giftCardId";
            return await _db.ExecuteScalar(sql, new { giftCardId });
        }

        public async Task<int> SumRedemptionsForSource(string sourceKind, Guid sourceId, Guid tenantId)
        {
            const string sql = @"SELECT COALESCE(SUM(amount_cents), 0)
                                 FROM gift_card_redemption
                                 WHERE source_kind = @sourceKind AND source_id = @sourceId AND tenant_id = @tenantId";
            return await _db.ExecuteScalar(sql, new { sourceKind, sourceId, tenantId });
        }

        public async Task<Guid> RecordRedemption(GiftCardRedemption r)
        {
            const string sql = @"
                INSERT INTO gift_card_redemption
                    (gift_card_id, tenant_id, user_id, source_kind, source_id, amount_cents)
                VALUES
                    (@GiftCardId, @TenantId, @UserId, @SourceKind, @SourceId, @AmountCents)
                RETURNING id";
            return (await _db.Query<Guid>(sql, r)).First();
        }

        public async Task<List<GiftCardRedemption>> ListRedemptionsByCard(Guid giftCardId)
        {
            const string sql = @"
                SELECT id, gift_card_id AS GiftCardId, tenant_id AS TenantId, user_id AS UserId,
                       source_kind AS SourceKind, source_id AS SourceId, amount_cents AS AmountCents,
                       redeemed_at AS RedeemedAt
                FROM gift_card_redemption
                WHERE gift_card_id = @giftCardId
                ORDER BY redeemed_at DESC";
            return (await _db.Query<GiftCardRedemption>(sql, new { giftCardId })).ToList();
        }
    }
}
