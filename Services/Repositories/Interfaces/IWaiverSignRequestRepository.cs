using Services.Repositories.Data.WaiverData;

namespace Services.Repositories.Interfaces
{
    public interface IWaiverSignRequestRepository
    {
        /// <summary>New signing link. The optional ticket / pass is what the gate sent it FOR
        /// (Script0304): signing it links the new signature there (see WaiverController.SignByToken).</summary>
        Task<WaiverSignRequestRow> Create(Guid tenantId, Guid? waiverId, string token,
            string recipientEmail, string? recipientName, Guid? eventId, Guid? requestedByUserId,
            Guid? eventTicketPurchaseId = null, Guid? seasonPassPurchaseId = null, SignRequestRider? rider = null);

        /// <summary>Of these rider accounts, the ones that already have an open (pending / sent /
        /// opened) link: one query, keyed by the rider, not the email.</summary>
        Task<HashSet<Guid>> RidersWithOpenRequests(Guid tenantId, IReadOnlyCollection<Guid> riderUserIds);

        /// <summary>The paid tickets (events not over) and passes (in season) an account holds, soonest first.</summary>
        Task<List<RiderCredentialRef>> UpcomingCredentialsForAccount(Guid tenantId, Guid userId);
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
