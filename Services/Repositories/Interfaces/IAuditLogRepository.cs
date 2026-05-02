using Services.Repositories.Data.AuditData;

namespace Services.Repositories.Interfaces
{
    public interface IAuditLogRepository
    {
        Task<Guid> Insert(AuditLogEntry entry);

        Task<List<AuditLogEntry>> List(
            string? action = null,
            Guid? actorUserId = null,
            string? targetKind = null,
            Guid? targetId = null,
            Guid? tenantId = null,
            DateTime? fromUtc = null,
            DateTime? toUtc = null,
            int take = 200);
    }
}
