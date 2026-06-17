using Services.Repositories.Data.UserData;

namespace Services.Repositories.Interfaces
{
    public interface ILoampassRedemptionRepository
    {
        Task Create(LoampassRedemption redemption);
        Task<LoampassRedemption?> GetByPurchaseId(Guid eventTicketPurchaseId, Guid tenantId);
        Task MarkRefunded(Guid id);
    }
}
