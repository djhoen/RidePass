namespace Services.Repositories.Data.CashData
{
    // Tips a worker has to settle, as the shift review and the turn-in snapshot read them: every
    // PAID, tipped F&B sale of theirs that no turn-in has claimed yet (Script0296), whenever it was
    // rung. Card is the two Stripe rails; the total also carries any other tender. The two counts
    // are informational and cover the review window (session open, or business-day start, to now),
    // so "sales this shift" keeps meaning sales this shift.
    public class WorkerTipTotals
    {
        public long TipsEarnedCents { get; set; }
        public long CardTipsCents { get; set; }
        public long CashSaleTipsCents { get; set; }
        // The part of TipsEarnedCents on sales paid before the open session began: a tab closed
        // after an earlier close-out, or a shift that never turned in. 0 when there is no session.
        public long TipsFromEarlierShiftsCents { get; set; }
        public int SaleCount { get; set; }
        public int CashSaleCount { get; set; }
    }

    // One worker's tip balance (CashRepository.TipsOwedSql), RAW: what the house has owed them
    // across their turn-ins and what a manager has since paid. The outstanding figure is never
    // computed here; ShiftCloseMath.OutstandingOwed is the one place that does it (reached through
    // webapi's TipsOwedRowDto.From), so every screen agrees.
    public class TipsOwedRow
    {
        public Guid UserId { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
        public long HouseOwesTotalCents { get; set; }
        public long PaidByManagerCents { get; set; }
        public long PaidFromDrawerCents { get; set; }
        public long EarnedTotalCents { get; set; }
        // Tips on paid sales no turn-in has claimed yet: waiting for the worker's next close-out.
        public long UnclaimedTipsCents { get; set; }
        public DateTime? LastShiftAt { get; set; }
        // Last turn-in that actually carried tips or house-owes, and last payout of any source:
        // the two "recent activity" signals the Tips owed page lists people by.
        public DateTime? LastTipShiftAt { get; set; }
        public DateTime? LastPayoutAt { get; set; }
    }

    // A payout with the recipient's and payer's display names resolved, for the payout history.
    // PaidByUserId is null once the payer's account has been deleted (Script0296: SET NULL).
    public class TipPayoutRow
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
        public int AmountCents { get; set; }
        public string Source { get; set; } = null!;
        public Guid? PaidByUserId { get; set; }
        public string? PaidByName { get; set; }
        public string? PaidByEmail { get; set; }
        public string? Note { get; set; }
        public Guid? CashTurnInId { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    // One cash settlement of house-owes, with who paid it out of their drawer and when, so the
    // reconciliation can take it out of that drawer's expected cash for the window it fell in.
    // Rows whose payer has since been removed are not returned: they belong to no drawer.
    public class CashPayoutRow
    {
        public Guid PayerUserId { get; set; }
        public DateTime CreatedAt { get; set; }
        public int AmountCents { get; set; }
    }
}
