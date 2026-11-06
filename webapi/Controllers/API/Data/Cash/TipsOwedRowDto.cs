using Services.Cash;
using Services.Helpers;
using Services.Repositories.Data.CashData;

namespace webapi.Controllers.API.Data.Cash
{
    // One worker's tip balance. OwedCents is derived (house-owes on their turn-ins minus manager
    // payouts), never stored, and never shown below zero. From() is the ONLY place that derives
    // it, through ShiftCloseMath.OutstandingOwed, for the Tips owed page, the pay-out guard, the
    // worker's own shift review and the End of Day report alike.
    public class TipsOwedRowDto
    {
        public Guid UserId { get; set; }
        public string Name { get; set; } = null!;
        public long OwedCents { get; set; }
        public DateTime? LastShiftAtUtc { get; set; }
        public long EarnedTotalCents { get; set; }
        public long PaidFromDrawerCents { get; set; }
        public long PaidByManagerCents { get; set; }
        // Tips on paid sales no turn-in has claimed yet: not owed until the worker closes out,
        // but a manager wants to see what is waiting.
        public long UnclaimedTipsCents { get; set; }

        public static TipsOwedRowDto From(TipsOwedRow r) => new()
        {
            UserId = r.UserId,
            Name = UserDisplayName.Of(r.Name, r.Email),
            OwedCents = ShiftCloseMath.OutstandingOwed(r.HouseOwesTotalCents, r.PaidByManagerCents),
            LastShiftAtUtc = r.LastShiftAt.HasValue ? TenantClock.AsUtc(r.LastShiftAt.Value) : null,
            EarnedTotalCents = r.EarnedTotalCents,
            PaidFromDrawerCents = r.PaidFromDrawerCents,
            PaidByManagerCents = r.PaidByManagerCents,
            UnclaimedTipsCents = r.UnclaimedTipsCents,
        };
    }
}
