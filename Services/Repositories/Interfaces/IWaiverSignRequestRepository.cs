using Services.Repositories.Data.WaiverData;

namespace Services.Repositories.Interfaces
{
    public interface IWaiverSignRequestRepository
    {
        Task<WaiverSignRequestRow> Create(Guid tenantId, Guid? waiverId, string token,
            string recipientEmail, string? recipientName, Guid? eventId, Guid? requestedByUserId);
        Task<(List<WaiverSignRequestRow> Rows, int Total)> List(Guid tenantId,
            string? search, string? status, int page, int pageSize);
        Task<WaiverSignRequestRow?> GetById(Guid id, Guid tenantId);

        /// <summary>Token lookup for the public signing page. Global (the token is the
        /// credential); the caller must verify the row's TenantId matches the resolved
        /// tenant before returning any data.</summary>
        Task<WaiverSignRequestRow?> GetByToken(string token);

        Task MarkSent(Guid id, Guid tenantId);
        Task MarkOpened(Guid id, Guid tenantId);
        Task MarkSigned(Guid id, Guid tenantId, Guid signatureId);
        Task Cancel(Guid id, Guid tenantId);

        /// <summary>Paid ticket holders on an event who have no signature on a currently
        /// active waiver and no open request yet, deduped by email.</summary>
        Task<List<WaiverRequestCandidate>> CandidatesForEvent(Guid eventId, Guid tenantId);

        /// <summary>Distinct paid-ticket purchaser emails on an event (the full roster size,
        /// for the bulk-send "already covered" arithmetic).</summary>
        Task<int> CountRosterEmails(Guid eventId, Guid tenantId);
    }
}
