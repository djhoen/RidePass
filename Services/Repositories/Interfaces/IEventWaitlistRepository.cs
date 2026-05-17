using Services.Repositories.Data.WaitlistData;

namespace Services.Repositories.Interfaces
{
    public interface IEventWaitlistRepository
    {
        // ── Joining the queue ────────────────────────────────────────────────
        /// <summary>
        /// Inserts a new waiting row at the back of the (event_id, tier_id) bucket.
        /// Returns the new id and final position. Throws on the unique-constraint
        /// violation when the rider already has a waiting/promoted row in this bucket.
        /// </summary>
        Task<(Guid Id, int Position)> Enqueue(EventWaitlistEntry entry);

        Task<EventWaitlistEntry?> GetById(Guid id);
        Task<EventWaitlistEntry?> GetByConfirmToken(Guid token);
        Task<EventWaitlistEntry?> GetByPrepayPaymentIntentId(string paymentIntentId);
        Task<EventWaitlistEntry?> GetActiveForUser(Guid eventId, Guid? tierId, Guid userId);
        Task<List<EventWaitlistEntry>> ListForEvent(Guid eventId);
        Task<List<EventWaitlistEntry>> ListMine(Guid userId, Guid tenantId);

        /// <summary>
        /// Picks the front-of-line waiting entry for a bucket (lowest position, status='waiting').
        /// Returns null if the bucket is empty.
        /// </summary>
        Task<EventWaitlistEntry?> PeekFront(Guid eventId, Guid? tierId);

        // ── State transitions ────────────────────────────────────────────────
        Task SetPrepayPaymentIntentId(Guid id, string paymentIntentId);
        Task MarkPrepaid(Guid id, int amountCents);

        /// <summary>Flip a waiting row to 'promoted' and set deadline + confirm token.</summary>
        Task MarkPromoted(Guid id, DateTime promotedAtUtc, DateTime confirmDeadlineUtc, Guid confirmToken);

        Task MarkExpired(Guid id);
        Task MarkCancelled(Guid id, string? reason);
        Task MarkConfirmed(Guid id, Guid createdPurchaseId, string createdPurchaseKind);
        Task SetPrepayRefund(Guid id, string refundId, DateTime atUtc);

        // ── Background worker ────────────────────────────────────────────────
        /// <summary>Promoted rows whose confirm_deadline has passed.</summary>
        Task<List<EventWaitlistEntry>> ListExpired(DateTime nowUtc, int take);

        /// <summary>How many waiting riders are ahead of me in this bucket?</summary>
        Task<int> CountAhead(Guid eventId, Guid? tierId, int myPosition);
    }
}
