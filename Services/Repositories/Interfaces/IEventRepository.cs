using Services.Repositories.Data.EventData;

namespace Services.Repositories.Interfaces
{
    public interface IEventRepository
    {
        Task<List<Event>> GetInRange(Guid tenantId, DateTime fromUtc, DateTime toUtc);
        Task<Event?> GetById(Guid id, Guid tenantId);
        Task<Guid> Create(Event ev);
        Task Update(Event ev);
        Task Delete(Guid id, Guid tenantId);
        Task<List<EventWithTypeContext>> GetUpcomingWithType(Guid tenantId, int limit);

        /// <summary>Events that use this waiver as their rider waiver, spectator waiver, or both.</summary>
        Task<List<EventWaiverAssociation>> ListByWaiverId(Guid waiverId, Guid tenantId);

        /// <summary>
        /// Atomically set whether this event uses the given waiver for the rider audience
        /// and/or the spectator audience. When attaching a role, also enables the
        /// matching `requires_*_waiver` flag so the buy flow actually enforces it.
        /// When detaching, only clears the column for that role if it currently points
        /// at the given waiver — so we don't accidentally unhook a different waiver.
        /// </summary>
        Task SetWaiverRole(Guid eventId, Guid tenantId, Guid waiverId, bool asRider, bool asSpectator);

        // ── Day-pass eligibility (which pass products can be redeemed at this event) ──
        Task<List<Guid>> ListEligiblePassProductIds(Guid eventId);
        Task<Dictionary<Guid, List<Guid>>> ListEligibilityForEvents(IEnumerable<Guid> eventIds);
        Task ReplacePassEligibility(Guid eventId, IEnumerable<Guid> productIds);
        Task<bool> IsPassProductEligible(Guid eventId, Guid productId);
    }
}
