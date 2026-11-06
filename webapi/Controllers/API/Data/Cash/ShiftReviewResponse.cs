using Services.Repositories.Data.CashData;

namespace webapi.Controllers.API.Data.Cash
{
    // A person's own close-out numbers (Toast "Shift Review"). Deliberately carries no expected
    // cash: the drawer turn-in is a blind count and a separate thing.
    //
    // TipsEarnedCents is what the next shift close will settle: every tip on the person's paid
    // sales that no shift close has claimed yet, however old (Script0296/0298). A tab closed after
    // the last close therefore lands here, not on nobody. TipsFromEarlierShiftsCents is the part
    // of it paid before the open session began, so the screen can say "includes $X from earlier".
    // The window is informational only (the person's open session, or the business day so far
    // when they never opened a drawer) and bounds the sale counts, not the tips.
    //
    // The two mode fields tell the dialog what to show: in manager_later mode there is no drawer
    // input and a manager pays the tips later; in drawer_at_close the person takes them from the
    // drawer. UserId/Name say whose review this is (the cashier token's person, or the signed-in
    // account).
    public class ShiftReviewResponse
    {
        public Guid UserId { get; set; }
        public string Name { get; set; } = null!;
        // The TERMINAL account's open drawer, resolved exactly as ShiftClose resolves it (the
        // session for ?eventId, else the no-event session): Session is that row or null,
        // DrawerOpen / DrawerSessionId are the same fact in the two shapes the dialog wants. The
        // window and TipsFromEarlierShiftsCents are measured from this drawer's opened_at.
        public CashSession? Session { get; set; }
        public bool DrawerOpen { get; set; }
        public Guid? DrawerSessionId { get; set; }
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
        // manager_later | drawer_at_close
        public string TipsPayoutMode { get; set; } = "manager_later";
        // off | per_shift | per_order
        public string StaffCodeMode { get; set; } = "off";
    }
}
