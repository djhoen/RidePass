using Services.Helpers.Interfaces;
using Services.Repositories.Data.WaitlistData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class EventWaitlistRepository : IEventWaitlistRepository
    {
        private const string Columns = @"
            id, tenant_id AS TenantId, event_id AS EventId, tier_id AS TierId,
            ladder_group AS LadderGroup, user_id AS UserId,
            position, quantity, notes,
            is_prepaid AS IsPrepaid,
            prepay_pi_id AS PrepayPiId,
            stripe_connected_account_id AS StripeConnectedAccountId,
            prepay_amount_cents AS PrepayAmountCents,
            prepay_refund_id AS PrepayRefundId,
            prepay_refunded_at_utc AS PrepayRefundedAtUtc,
            promoted_at_utc AS PromotedAtUtc,
            confirm_deadline_utc AS ConfirmDeadlineUtc,
            confirm_token AS ConfirmToken,
            created_purchase_id AS CreatedPurchaseId,
            created_purchase_kind AS CreatedPurchaseKind,
            status, cancelled_reason AS CancelledReason,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        private readonly IDbHelper _db;
        public EventWaitlistRepository(IDbHelper db) => _db = db;

        public async Task<(Guid Id, int Position)> Enqueue(EventWaitlistEntry e)
        {
            // Two-step (compute position + insert) is fine for low contention; the
            // unique-active index keeps a rider from queueing twice in the same
            // bucket regardless. If two enqueues land at the same time and
            // compute the same position, the insert just shifts to position+1
            // on retry — for MVP we accept the rare duplicate-position outcome
            // since the queue still orders by position+created_at.
            // Position counts within the CLASS bucket: the ladder group when set, else the
            // exact tier (matching the per-event/standalone behavior). Keeps a ladder's steps
            // in one queue instead of separate per-step counters.
            const string nextPosSql = @"
                SELECT COALESCE(MAX(position), 0) + 1 FROM event_waitlist
                WHERE event_id = @EventId
                  AND ((@LadderGroup IS NOT NULL AND ladder_group = @LadderGroup)
                       OR (@LadderGroup IS NULL AND ladder_group IS NULL AND tier_id IS NOT DISTINCT FROM @TierId))
                  AND status IN ('waiting','promoted')";
            var pos = (await _db.Query<int>(nextPosSql, e)).First();
            e.Position = pos;

            const string insert = @"
                INSERT INTO event_waitlist
                    (tenant_id, event_id, tier_id, ladder_group, user_id, position, quantity, notes,
                     is_prepaid, prepay_pi_id, prepay_amount_cents,
                     status)
                VALUES
                    (@TenantId, @EventId, @TierId, @LadderGroup, @UserId, @Position, @Quantity, @Notes,
                     @IsPrepaid, @PrepayPiId, @PrepayAmountCents,
                     @Status)
                RETURNING id";
            var id = (await _db.Query<Guid>(insert, e)).First();
            return (id, pos);
        }

        public async Task<EventWaitlistEntry?> GetById(Guid id)
        {
            var sql = $"SELECT {Columns} FROM event_waitlist WHERE id = @id LIMIT 1";
            return (await _db.Query<EventWaitlistEntry>(sql, new { id })).FirstOrDefault();
        }

        public async Task<EventWaitlistEntry?> GetByConfirmToken(Guid token)
        {
            var sql = $"SELECT {Columns} FROM event_waitlist WHERE confirm_token = @token LIMIT 1";
            return (await _db.Query<EventWaitlistEntry>(sql, new { token })).FirstOrDefault();
        }

        public async Task<EventWaitlistEntry?> GetByPrepayPaymentIntentId(string paymentIntentId)
        {
            var sql = $"SELECT {Columns} FROM event_waitlist WHERE prepay_pi_id = @paymentIntentId LIMIT 1";
            return (await _db.Query<EventWaitlistEntry>(sql, new { paymentIntentId })).FirstOrDefault();
        }

        public async Task<EventWaitlistEntry?> GetActiveForUser(Guid eventId, Guid? tierId, string? ladderGroup, Guid userId)
        {
            var sql = $@"
                SELECT {Columns} FROM event_waitlist
                WHERE event_id = @eventId
                  AND ((@ladderGroup IS NOT NULL AND ladder_group = @ladderGroup)
                       OR (@ladderGroup IS NULL AND ladder_group IS NULL AND tier_id IS NOT DISTINCT FROM @tierId))
                  AND user_id = @userId
                  AND status IN ('waiting','promoted')
                LIMIT 1";
            return (await _db.Query<EventWaitlistEntry>(sql, new { eventId, tierId, ladderGroup, userId })).FirstOrDefault();
        }

        public async Task<List<EventWaitlistEntry>> ListForEvent(Guid eventId)
        {
            var sql = $@"
                SELECT {Columns} FROM event_waitlist
                WHERE event_id = @eventId
                ORDER BY tier_id NULLS FIRST, position";
            return (await _db.Query<EventWaitlistEntry>(sql, new { eventId })).ToList();
        }

        public async Task<List<EventWaitlistEntry>> ListMine(Guid userId, Guid tenantId)
        {
            var sql = $@"
                SELECT {Columns} FROM event_waitlist
                WHERE tenant_id = @tenantId AND user_id = @userId
                ORDER BY created_at DESC";
            return (await _db.Query<EventWaitlistEntry>(sql, new { tenantId, userId })).ToList();
        }

        public async Task<EventWaitlistEntry?> PeekFront(Guid eventId, Guid? tierId, string? ladderGroup)
        {
            var sql = $@"
                SELECT {Columns} FROM event_waitlist
                WHERE event_id = @eventId
                  AND ((@ladderGroup IS NOT NULL AND ladder_group = @ladderGroup)
                       OR (@ladderGroup IS NULL AND ladder_group IS NULL AND tier_id IS NOT DISTINCT FROM @tierId))
                  AND status = 'waiting'
                ORDER BY position
                LIMIT 1";
            return (await _db.Query<EventWaitlistEntry>(sql, new { eventId, tierId, ladderGroup })).FirstOrDefault();
        }

        public async Task SetPrepayPaymentIntentId(Guid id, string paymentIntentId)
        {
            const string sql = "UPDATE event_waitlist SET prepay_pi_id = @paymentIntentId WHERE id = @id";
            await _db.Execute(sql, new { id, paymentIntentId });
        }

        // Direct charge: snapshot the connected account the pre-pay was charged on so the promoter
        // can stamp the ticket it creates (for correct refunds).
        public async Task MarkPrepayDirectCharge(Guid id, Guid tenantId, string connectedAccountId)
        {
            const string sql = @"
                UPDATE event_waitlist
                SET stripe_connected_account_id = @connectedAccountId
                WHERE id = @id AND tenant_id = @tenantId";
            await _db.Execute(sql, new { id, tenantId, connectedAccountId });
        }

        public async Task MarkPrepaid(Guid id, int amountCents)
        {
            const string sql = @"
                UPDATE event_waitlist
                SET is_prepaid = true, prepay_amount_cents = @amountCents
                WHERE id = @id";
            await _db.Execute(sql, new { id, amountCents });
        }

        public async Task MarkPromoted(Guid id, DateTime promotedAtUtc, DateTime confirmDeadlineUtc, Guid confirmToken)
        {
            const string sql = @"
                UPDATE event_waitlist
                SET status = 'promoted',
                    promoted_at_utc = @promotedAtUtc,
                    confirm_deadline_utc = @confirmDeadlineUtc,
                    confirm_token = @confirmToken
                WHERE id = @id AND status = 'waiting'";
            await _db.Execute(sql, new { id, promotedAtUtc, confirmDeadlineUtc, confirmToken });
        }

        public async Task MarkExpired(Guid id)
        {
            const string sql = @"
                UPDATE event_waitlist
                SET status = 'expired'
                WHERE id = @id AND status = 'promoted'";
            await _db.Execute(sql, new { id });
        }

        public async Task MarkCancelled(Guid id, string? reason)
        {
            const string sql = @"
                UPDATE event_waitlist
                SET status = 'cancelled', cancelled_reason = @reason
                WHERE id = @id AND status IN ('waiting','promoted')";
            await _db.Execute(sql, new { id, reason });
        }

        public async Task MarkConfirmed(Guid id, Guid createdPurchaseId, string createdPurchaseKind)
        {
            const string sql = @"
                UPDATE event_waitlist
                SET status = 'confirmed',
                    created_purchase_id = @createdPurchaseId,
                    created_purchase_kind = @createdPurchaseKind
                WHERE id = @id AND status IN ('waiting','promoted')";
            await _db.Execute(sql, new { id, createdPurchaseId, createdPurchaseKind });
        }

        public async Task SetPrepayRefund(Guid id, string refundId, DateTime atUtc)
        {
            const string sql = @"
                UPDATE event_waitlist
                SET prepay_refund_id = @refundId, prepay_refunded_at_utc = @atUtc
                WHERE id = @id";
            await _db.Execute(sql, new { id, refundId, atUtc });
        }

        public async Task<List<EventWaitlistEntry>> ListExpired(DateTime nowUtc, int take)
        {
            var sql = $@"
                SELECT {Columns} FROM event_waitlist
                WHERE status = 'promoted'
                  AND confirm_deadline_utc <= @nowUtc
                ORDER BY confirm_deadline_utc
                LIMIT @take";
            return (await _db.Query<EventWaitlistEntry>(sql, new { nowUtc, take })).ToList();
        }

        public async Task<int> CountAhead(Guid eventId, Guid? tierId, string? ladderGroup, int myPosition)
        {
            const string sql = @"
                SELECT COUNT(*) FROM event_waitlist
                WHERE event_id = @eventId
                  AND ((@ladderGroup IS NOT NULL AND ladder_group = @ladderGroup)
                       OR (@ladderGroup IS NULL AND ladder_group IS NULL AND tier_id IS NOT DISTINCT FROM @tierId))
                  AND status = 'waiting'
                  AND position < @myPosition";
            return await _db.ExecuteScalar(sql, new { eventId, tierId, ladderGroup, myPosition });
        }
    }
}
