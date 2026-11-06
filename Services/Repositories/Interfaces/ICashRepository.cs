using Services.Repositories.Data.CashData;
using Services.Repositories.Data.PaymentData;

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

        // ── Turn-ins ─────────────────────────────────────────────────────────────
        /// <summary>
        /// The whole shift close in one transaction: insert the turn-in, claim every unclaimed tip
        /// of the worker's paid before <paramref name="claimAsOfUtc"/> onto it, snapshot
        /// tips_earned / card_tips / house_owes FROM THE CLAIMED ROWS (the caller's
        /// TipsFromDrawerCents and CashTipsDeclaredCents are taken as given), move its session out
        /// of 'open', and, when the worker took tips from the drawer, write the 'drawer' tip_payout
        /// plus its ledger row. All or nothing. Returns the turn-in id. Throws
        /// <see cref="Services.Cash.TipsChangedDuringCloseException"/> (and lands nothing) when the
        /// claimed tips no longer cover TipsFromDrawerCents.
        /// </summary>
        Task<Guid> SubmitTurnIn(CashTurnIn turnIn, DateTime claimAsOfUtc, TipPayout? drawerPayout, TenantLedgerEntry? payoutLedgerRow);
        Task<CashTurnIn?> GetTurnInById(Guid id, Guid tenantId);
        Task ConfirmTurnIn(Guid id, Guid tenantId, Guid managerUserId, int managerCountedCents, string? note);
        Task<List<CashTurnIn>> ListPendingTurnIns(Guid tenantId, Guid? eventId);
        Task<List<CashTurnIn>> ListTurnInsByEvent(Guid tenantId, Guid eventId);
        /// <summary>Turn-ins with no event, bounded by submitted_at.</summary>
        Task<List<CashTurnIn>> ListTurnInsWithoutEvent(Guid tenantId, DateTime fromUtc, DateTime toUtc);

        // ── Tips ─────────────────────────────────────────────────────────────────
        /// <summary>
        /// Tips the worker has to settle: every PAID, tipped sale of theirs that no turn-in has
        /// claimed, paid before <paramref name="asOfUtc"/>, with no lower bound. Split card vs cash
        /// tender, plus the part paid before <paramref name="sessionOpenedAtUtc"/> (0 when null).
        /// The sale counts cover [<paramref name="windowFromUtc"/>, asOf) and are informational.
        /// </summary>
        Task<WorkerTipTotals> GetWorkerTipTotals(Guid tenantId, Guid userId, DateTime asOfUtc, DateTime windowFromUtc, DateTime? sessionOpenedAtUtc);
        /// <summary>
        /// A manager paying tips owed: the tip_payout row and its ledger row (cash tender for a
        /// cash payout, 'payroll' tender for a payroll one) in one transaction. Returns the payout id.
        /// </summary>
        Task<Guid> CreateTipPayout(TipPayout payout, TenantLedgerEntry? ledgerRow);
        /// <summary>
        /// Per-worker tip balances, raw (house-owes on turn-ins and manager payouts; the caller
        /// derives what is outstanding). Rows are everyone still owed anything, anyone with tips
        /// no turn-in has claimed yet, plus anyone with a tipped shift or a payout since
        /// <paramref name="activitySinceUtc"/>.
        /// </summary>
        Task<List<TipsOwedRow>> ListTipsOwed(Guid tenantId, DateTime activitySinceUtc);
        /// <summary>One worker's balance row, or null when they have no tip history at all.</summary>
        Task<TipsOwedRow?> GetTipsOwedRow(Guid tenantId, Guid userId);
        /// <summary>
        /// Every worker's outstanding balance summed in the database, over the same definitions
        /// ListTipsOwed reads, so the End of Day headline equals the sum of the Tips owed rows.
        /// </summary>
        Task<long> SumOutstandingOwed(Guid tenantId);
        /// <summary>
        /// Every cash settlement of house-owes in [fromUtc, toUtc) (tip_payout source 'cash') with
        /// who paid it and when, so a reconciliation can take each one out of the payer's own
        /// drawer for the window it fell in, from one read.
        /// </summary>
        Task<List<CashPayoutRow>> ListCashPayoutsPaidBy(Guid tenantId, DateTime fromUtc, DateTime toUtc);
        /// <summary>Payout history (every source) since a time, newest first, at most <paramref name="take"/> rows.</summary>
        Task<List<TipPayoutRow>> ListTipPayouts(Guid tenantId, DateTime sinceUtc, int take = 500);
        /// <summary>Exact total a manager has paid out (cash + payroll) since a time, whatever the history cap.</summary>
        Task<long> SumManagerPayouts(Guid tenantId, DateTime sinceUtc);
        /// <summary>The distinct tip_payout sources this tenant has ever logged (drawer | cash | payroll); empty when none.</summary>
        Task<List<string>> TipPayoutSources(Guid tenantId);
    }
}
