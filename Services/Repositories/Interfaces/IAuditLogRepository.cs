using Services.Repositories.Data.AuditData;

namespace Services.Repositories.Interfaces
{
    public interface IAuditLogRepository
    {
        Task<Guid> Insert(AuditLogEntry entry);

        /// <summary>
        /// Unfiltered read for SUPER ADMIN use. A null <paramref name="tenantId"/> deliberately
        /// means "every tenant", so this must never back a tenant-facing endpoint. Tenant-facing
        /// callers use <see cref="ListForTenant"/>, which cannot express that.
        /// </summary>
        Task<List<AuditLogEntry>> List(
            string? action = null,
            Guid? actorUserId = null,
            string? targetKind = null,
            Guid? targetId = null,
            Guid? tenantId = null,
            DateTime? fromUtc = null,
            DateTime? toUtc = null,
            int take = 200);

        /// <summary>
        /// Tenant-scoped read for the staff activity view. tenantId is non-nullable on purpose:
        /// the tenant predicate cannot be omitted or accidentally passed null, so this overload
        /// can never return another tenant's rows.
        /// </summary>
        Task<List<AuditLogEntry>> ListForTenant(
            Guid tenantId,
            string? action = null,
            Guid? actorUserId = null,
            DateTime? fromUtc = null,
            DateTime? toUtc = null,
            int take = 200);

        /// <summary>
        /// The (staff member, address) pairs this tenant has already seen before
        /// <paramref name="beforeUtc"/>, for the "new address" tripwire. Bounded by a lookback so
        /// an address someone used once a year ago doesn't count as familiar forever.
        /// </summary>
        Task<HashSet<(Guid ActorUserId, string Ip)>> ListKnownActorAddresses(
            Guid tenantId, DateTime beforeUtc, int lookbackDays);
    }
}
