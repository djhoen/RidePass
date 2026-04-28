using Services.Repositories.Data.PaymentData;

namespace Services.Repositories.Interfaces
{
    public interface IDisputeRepository
    {
        Task Upsert(Dispute dispute);
        Task<Dispute?> GetByStripeDisputeId(string stripeDisputeId);
        Task<List<DisputeWithContext>> ListByTenant(Guid tenantId);
        Task<List<DisputeWithContext>> ListAllAcrossTenants();
    }
}
