namespace Services.Repositories.Data.CashData
{
    // One payment of tips to a staff member (Script0294). 'drawer' rows are written at shift
    // close, when the worker takes their tips out of their own drawer (PaidByUserId is the
    // worker); 'cash' and 'payroll' rows are a manager settling house-owes later. Every payout has
    // a 'tip_payout' ledger row behind it (LedgerEntryId): cash tender for the two cash kinds,
    // 'payroll' tender for a payroll one, which moves no cash but still has to leave Tips payable
    // in the books. What a worker is still owed is never stored: it is
    // SUM(cash_turn_in.house_owes_cents) minus the 'cash' + 'payroll' rows here. PaidByUserId goes
    // null if the payer's account is deleted (Script0296); the payout itself stays, because the
    // balance is derived from it.
    public class TipPayout
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid UserId { get; set; }                 // recipient
        public Guid? CashTurnInId { get; set; }          // the close this came out of, for 'drawer'
        public int AmountCents { get; set; }
        public string Source { get; set; } = "drawer";   // drawer | cash | payroll
        public Guid? PaidByUserId { get; set; }          // worker (drawer) or manager; null once removed
        public Guid? LedgerEntryId { get; set; }
        public string? Note { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
