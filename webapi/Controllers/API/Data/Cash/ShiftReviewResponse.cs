using Services.Repositories.Data.CashData;

namespace webapi.Controllers.API.Data.Cash
{
    // The worker's own close-out numbers (Toast "Shift Review"). Deliberately carries no expected
    // cash: the turn-in is a blind count.
    //
    // TipsEarnedCents is what the next turn-in will settle: every tip on the worker's paid sales
    // that no turn-in has claimed yet, however old (Script0296). A tab closed after the last
    // close-out therefore lands here, not on nobody. TipsFromEarlierShiftsCents is the part of it
    // paid before the open session began, so the screen can say "includes $X from earlier". The
    // window is informational only (the open session, or the business day so far when the worker
    // never opened a drawer) and bounds the sale counts, not the tips.
    public class ShiftReviewResponse
    {
        public CashSession? Session { get; set; }
        public DateTime WindowFromUtc { get; set; }
        public DateTime WindowToUtc { get; set; }
        public long TipsEarnedCents { get; set; }
        public long CardTipsCents { get; set; }
        public long CashSaleTipsCents { get; set; }
        // Part of TipsEarnedCents on sales paid before the session opened. 0 with no session.
        public long TipsFromEarlierShiftsCents { get; set; }
        public int SaleCount { get; set; }
        public int CashSaleCount { get; set; }
        // Outstanding house-owes from earlier shifts. Never negative.
        public long OwedToYouCents { get; set; }
    }
}
