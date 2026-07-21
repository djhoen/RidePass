using Services.Repositories.Data.CashData;

namespace Services.Repositories.Interfaces
{
    public interface ICashRepository
    {
        // ── Sessions ─────────────────────────────────────────────────────────────
        Task<CashSession?> GetOpenSession(Guid tenantId, Guid userId, Guid? eventId);
        Task<CashSession?> GetSessionById(Guid id, Guid tenantId);
        Task<List<CashSession>> ListSessionsByEvent(Guid tenantId, Guid eventId);
        /// <summary>Sessions with no event (a shop or F&B shift), bounded by opened_at.</summary>
        Task<List<CashSession>> ListSessionsWithoutEvent(Guid tenantId, DateTime fromUtc, DateTime toUtc);
        Task<Guid> CreateSession(CashSession session);
        Task SetSessionStatus(Guid id, Guid tenantId, string status);

        // ── Turn-ins ─────────────────────────────────────────────────────────────
        Task<Guid> CreateTurnIn(CashTurnIn turnIn);
        Task<CashTurnIn?> GetTurnInById(Guid id, Guid tenantId);
        Task ConfirmTurnIn(Guid id, Guid tenantId, Guid managerUserId, int managerCountedCents, string? note);
        Task<List<CashTurnIn>> ListPendingTurnIns(Guid tenantId, Guid? eventId);
        Task<List<CashTurnIn>> ListTurnInsByEvent(Guid tenantId, Guid eventId);
        /// <summary>Turn-ins with no event, bounded by submitted_at.</summary>
        Task<List<CashTurnIn>> ListTurnInsWithoutEvent(Guid tenantId, DateTime fromUtc, DateTime toUtc);
    }
}
