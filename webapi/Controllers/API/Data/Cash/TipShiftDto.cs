using Services.Helpers;
using Services.Repositories.Data.CashData;

namespace webapi.Controllers.API.Data.Cash
{
    // A person's shift report as POST Cash/ShiftClose returns it: the tip_shift row, camelCased,
    // plus the person's display name.
    public class TipShiftDto
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid UserId { get; set; }
        public string Name { get; set; } = null!;
        public Guid? ClosedByUserId { get; set; }
        public Guid? CashSessionId { get; set; }
        // yyyy-MM-dd, the tenant's local date of the close.
        public string BusinessDate { get; set; } = null!;
        public DateTime ClosedAt { get; set; }
        public int TipsEarnedCents { get; set; }
        public int CardTipsCents { get; set; }
        public int CashTipsDeclaredCents { get; set; }
        public int TipsFromDrawerCents { get; set; }
        public int HouseOwesCents { get; set; }
        public string? Note { get; set; }

        public static TipShiftDto From(TipShiftRow s) => new()
        {
            Id = s.Id,
            TenantId = s.TenantId,
            UserId = s.UserId,
            Name = UserDisplayName.Of(s.Name, s.Email),
            ClosedByUserId = s.ClosedByUserId,
            CashSessionId = s.CashSessionId,
            BusinessDate = s.BusinessDate.ToString("yyyy-MM-dd"),
            ClosedAt = TenantClock.AsUtc(s.ClosedAt),
            TipsEarnedCents = s.TipsEarnedCents,
            CardTipsCents = s.CardTipsCents,
            CashTipsDeclaredCents = s.CashTipsDeclaredCents,
            TipsFromDrawerCents = s.TipsFromDrawerCents,
            HouseOwesCents = s.HouseOwesCents,
            Note = s.Note,
        };
    }
}
