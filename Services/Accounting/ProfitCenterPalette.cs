namespace Services.Accounting
{
    /// <summary>
    /// The categorical color slots profit centers are drawn in, everywhere they appear: the
    /// Profit Centers settings page, the End of Day report, Revenue by Department, the Sales
    /// Summary chart and the QuickBooks mapping screen. One list, so a center is the same color
    /// on every screen.
    ///
    /// ── Why these eight, in this order ───────────────────────────────────────────────────
    /// This is a validated categorical palette: the slot ORDER is the colorblind-safety
    /// mechanism, not decoration. Adjacent slots were checked for CVD separation (protan /
    /// deutan / tritan), normal-vision separation, a lightness band and a chroma floor, against
    /// the surfaces these charts actually render on (Vuetify light #FFFFFF, dark #212121).
    /// Re-order or substitute a hue and that guarantee is gone, so don't hand-edit one value.
    ///
    /// Slot 1 (blue) is RESERVED for a total / all-revenue series and is never handed out as a
    /// profit center's default: the Sales Summary chart draws overall revenue in blue with each
    /// center beside it, so a center painted the same blue would read as the total.
    ///
    /// ── Light and dark are two selected steps of the same hue ────────────────────────────
    /// Not an automatic lightening. Reusing the light hexes on the dark surface genuinely fails:
    /// violet #4a3aa7 lands at 1.88:1 there, which is unreadable. <see cref="DarkVariantFor"/>
    /// maps a palette color to the step chosen for the dark surface. A CUSTOM color the tenant
    /// picked passes through unchanged, since there is no principled way to re-step an arbitrary
    /// hex; that is the documented cost of letting them choose freely.
    /// </summary>
    public static class ProfitCenterPalette
    {
        /// <summary>Slot 1. Reserved for totals / all-revenue series, never a center default.</summary>
        public const string TotalSeries = "#2a78d6";
        /// <summary>Dark-surface step of <see cref="TotalSeries"/>.</summary>
        public const string TotalSeriesDark = "#3987e5";

        /// <summary>
        /// Fallback for a center with no color and for anything past the palette. Deliberately a
        /// neutral gray rather than a ninth invented hue: a generated color would claim a
        /// distinction the palette cannot actually guarantee, and gray reads as "unset", which
        /// is what it is.
        /// </summary>
        public const string Unassigned = "#8a8a8a";
        public const string UnassignedDark = "#9a9a9a";

        /// <summary>
        /// Center defaults, in assignment order: slots 2-8 of the validated palette (blue is held
        /// back for totals). Seven distinct colors; an eighth center gets <see cref="Unassigned"/>
        /// and the tenant is expected to pick one.
        /// </summary>
        public static readonly string[] Slots =
        {
            "#eb6834",   // orange
            "#1baf7a",   // aqua
            "#eda100",   // yellow
            "#e87ba4",   // magenta
            "#008300",   // green
            "#4a3aa7",   // violet
            "#e34948",   // red
        };

        private static readonly Dictionary<string, string> DarkBySlot = new(StringComparer.OrdinalIgnoreCase)
        {
            ["#2a78d6"] = "#3987e5",   // blue
            ["#eb6834"] = "#d95926",   // orange
            ["#1baf7a"] = "#199e70",   // aqua
            ["#eda100"] = "#c98500",   // yellow
            ["#e87ba4"] = "#d55181",   // magenta
            ["#008300"] = "#008300",   // green, same step in both modes
            ["#4a3aa7"] = "#9085e9",   // violet
            ["#e34948"] = "#e66767",   // red
            ["#8a8a8a"] = "#9a9a9a",   // unassigned gray
        };

        /// <summary>
        /// The default color for the nth center a tenant creates. Past the palette every center
        /// gets the neutral gray rather than a cycled hue: cycling would give two centers the
        /// same color on the same chart, which is worse than an obviously-unset one.
        /// </summary>
        public static string DefaultForIndex(int index) =>
            index >= 0 && index < Slots.Length ? Slots[index] : Unassigned;

        /// <summary>
        /// The first palette color not already in use, so a new center is visually distinct from
        /// the tenant's existing ones without them having to think about it.
        /// </summary>
        public static string FirstUnused(IEnumerable<string?> inUse)
        {
            var taken = inUse
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c!.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return Slots.FirstOrDefault(s => !taken.Contains(s)) ?? Unassigned;
        }

        /// <summary>
        /// The dark-surface step for a palette color; any other hex is returned unchanged.
        /// </summary>
        public static string DarkVariantFor(string color) =>
            DarkBySlot.TryGetValue(color?.Trim() ?? string.Empty, out var dark) ? dark : color;

        /// <summary>#RRGGBB only. Deliberately strict: shorthand and named colors would have to be
        /// normalised before they could be compared or re-stepped.</summary>
        public static bool IsValid(string? color) =>
            !string.IsNullOrWhiteSpace(color)
            && System.Text.RegularExpressions.Regex.IsMatch(color.Trim(), "^#[0-9a-fA-F]{6}$");
    }
}
