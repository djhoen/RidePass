using Services.Repositories.Data.UserData;

namespace Services.Repositories.Interfaces
{
    public interface IRiderLoampassLinkRepository
    {
        /// <summary>All LoamMx accounts this rider has linked (1 rider -> many accounts).</summary>
        Task<List<RiderLoampassLink>> ListByUserId(Guid userId, Guid tenantId);
        /// <summary>Link a LoamMx account to the rider; re-linking the same account refreshes it.</summary>
        Task Add(RiderLoampassLink link);
        /// <summary>Unlink one specific LoamMx account from the rider.</summary>
        Task DeleteByAccount(Guid userId, Guid tenantId, string loampassAccountId);
        /// <summary>The RidePass rider that has this LoamMx account linked, for a gate scan. Null if none.</summary>
        Task<Guid?> GetUserIdByAccount(string loampassAccountId, Guid tenantId);
    }
}
