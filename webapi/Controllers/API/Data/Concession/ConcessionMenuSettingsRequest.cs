using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Concession
{
    // Per-tenant menu board styling. Null logo/colors fall back to the tenant brand in the UI.
    public class ConcessionMenuSettingsRequest
    {
        public string? LogoUrl { get; set; }
        [MaxLength(32)] public string? BackgroundColor { get; set; }
        [MaxLength(32)] public string? TextColor { get; set; }
        [MaxLength(32)] public string? AccentColor { get; set; }
        public bool ShowCarousel { get; set; } = true;
        [Range(2, 60)]
        public int CarouselSeconds { get; set; } = 5;
        public bool TipsEnabled { get; set; }
        // Cook-screen color-escalation targets (minutes). Amber after warn, red after late.
        [Range(1, 240)] public int PrepWarnMinutes { get; set; } = 5;
        [Range(1, 240)] public int PrepLateMinutes { get; set; } = 10;
        // 7 entries (Sun..Sat) to limit online ordering to set hours, or null for always open.
        public List<ConcessionOrderingHoursDay>? OrderingHours { get; set; }
        // Open-season date ranges to limit online ordering, or null/empty for year-round.
        public List<ConcessionOrderingSeason>? OrderingSeasons { get; set; }
        // When true, online ordering is closed on days with nothing on the events calendar.
        public bool RequireEventDay { get; set; } = true;
        // When true, item prices already include sales tax (tax is backed out); false = tax added on top.
        public bool PricesIncludeTax { get; set; }
        // Member-perk discounts. Kind 'percent' (Value = bps, 0..10000) or 'amount' (Value = cents).
        public bool SeasonPassDiscountEnabled { get; set; }
        public string SeasonPassDiscountKind { get; set; } = "percent";
        [Range(0, 10_000_000)] public int SeasonPassDiscountValue { get; set; }
        public bool LoampassDiscountEnabled { get; set; }
        public string LoampassDiscountKind { get; set; } = "percent";
        [Range(0, 10_000_000)] public int LoampassDiscountValue { get; set; }
        // When true (default), an arbitrary manual discount requires a manager PIN.
        public bool RequireManagerForManualDiscount { get; set; } = true;
    }
}
