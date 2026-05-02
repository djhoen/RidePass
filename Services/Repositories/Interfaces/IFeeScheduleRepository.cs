using Services.Repositories.Data.PaymentData;

namespace Services.Repositories.Interfaces
{
    public interface IFeeScheduleRepository
    {
        /// <summary>
        /// Returns the schedule (with tiers) that is in effect for a tenant at the given UTC time.
        /// </summary>
        Task<TenantFeeScheduleWithTiers?> GetActive(Guid tenantId, DateTime atUtc);

        Task<TenantFeeScheduleWithTiers?> GetById(Guid scheduleId, Guid tenantId);

        Task<List<TenantFeeSchedule>> ListByTenant(Guid tenantId);

        /// <summary>
        /// Inserts a new schedule plus tiers atomically. Closes the previous active schedule
        /// (sets its effective_to_utc to the new schedule's effective_from_utc).
        /// </summary>
        Task<Guid> Replace(TenantFeeSchedule schedule, IEnumerable<TenantFeeTier> tiers);
    }
}
