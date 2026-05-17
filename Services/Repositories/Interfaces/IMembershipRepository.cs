using Services.Repositories.Data.MembershipData;

namespace Services.Repositories.Interfaces
{
    public interface IMembershipRepository
    {
        Task<Guid> Create(MembershipPurchase p);
        Task<MembershipPurchase?> GetById(Guid id);
        Task<MembershipPurchase?> GetByPaymentIntentId(string paymentIntentId);

        /// <summary>Most recent paid + still-valid membership for (user, tenant). Lifetime memberships have null valid_to_utc.</summary>
        Task<MembershipPurchase?> GetActive(Guid userId, Guid tenantId, DateTime nowUtc);

        Task<List<MembershipPurchase>> ListMine(Guid userId, Guid tenantId);
        Task<List<MembershipPurchase>> ListForTenant(Guid tenantId);

        Task SetStripePaymentIntentId(Guid id, string paymentIntentId);
        Task UpdateStatus(Guid id, string status);
    }
}
