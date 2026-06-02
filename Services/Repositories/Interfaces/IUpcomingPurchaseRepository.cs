using Services.Repositories.Data.MeData;

namespace Services.Repositories.Interfaces
{
    /// <summary>
    /// Cross-tenant query for everything a rider has coming up: paid event
    /// tickets for future events, paid day passes for today or later, valid
    /// season passes, and valid memberships, across every tenant the rider
    /// has ever purchased from. Scoped by the rider's user id (from the JWT
    /// at the controller), NOT by tenant_id — this is intentionally an apex
    /// (cross-tenant) feature for the platform landing page after sign-in.
    /// </summary>
    public interface IUpcomingPurchaseRepository
    {
        Task<List<UpcomingPurchaseRow>> ListForUser(Guid userId);
    }
}
