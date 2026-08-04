namespace webapi.Controllers.API.Data.Concession
{
    public class ConcessionMenuSettingsResponse
    {
        public string? LogoUrl { get; set; }
        public string? BackgroundColor { get; set; }
        public string? TextColor { get; set; }
        public string? AccentColor { get; set; }
        public bool ShowCarousel { get; set; }
        public int CarouselSeconds { get; set; }
        public bool TipsEnabled { get; set; }
        public bool CustomerDisplayEnabled { get; set; }
        public int PrepWarnMinutes { get; set; } = 5;
        public int PrepLateMinutes { get; set; } = 10;
        // Null = always open. Otherwise 7 entries (Sun..Sat). OrderingOpenNow is computed in the tenant tz.
        public List<ConcessionOrderingHoursDay>? OrderingHours { get; set; }
        // Null/empty = open year-round. Otherwise open-season date ranges (inclusive, "yyyy-MM-dd").
        public List<ConcessionOrderingSeason>? OrderingSeasons { get; set; }
        // When true, online ordering is closed on days with nothing on the events calendar.
        public bool RequireEventDay { get; set; } = true;
        // When true, item prices already include sales tax (tax is backed out); false = tax added on top.
        public bool PricesIncludeTax { get; set; }
        // Member-perk discounts. Kind 'percent' (Value = bps) or 'amount' (Value = cents).
        public bool SeasonPassDiscountEnabled { get; set; }
        public string SeasonPassDiscountKind { get; set; } = "percent";
        public int SeasonPassDiscountValue { get; set; }
        public bool LoampassDiscountEnabled { get; set; }
        public string LoampassDiscountKind { get; set; } = "percent";
        public int LoampassDiscountValue { get; set; }
        // When true (default), an arbitrary manual discount requires a manager PIN.
        public bool RequireManagerForManualDiscount { get; set; } = true;
        // Whether the tenant has already loaded the starter catalog (hides the "Load starter content" button).
        public bool StarterSeeded { get; set; }
        // Combined gate: in open season AND (event today OR not required) AND within today's hours.
        public bool OrderingOpenNow { get; set; } = true;
    }
}
