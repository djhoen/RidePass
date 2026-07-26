namespace Services.Repositories.Data.DiscountData
{
    /// <summary>
    /// A discount a staff member can apply at a counter ("Military 10%", "VMBA member", "$2 off").
    /// Tenant-defined, and the tenant decides both where it applies and whether taking it needs a
    /// manager's PIN.
    /// </summary>
    public class DiscountPreset
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = null!;

        /// <summary>'percent' or 'amount'.</summary>
        public string Kind { get; set; } = "percent";

        /// <summary>Basis points when Kind is 'percent' (1000 = 10%), cents when 'amount'.</summary>
        public int Value { get; set; }

        /// <summary>Where this may be applied. Members are ledger source_kind values, so a
        /// discount's surface compares directly against what a sale books itself as.</summary>
        public string[] Surfaces { get; set; } = Array.Empty<string>();

        /// <summary>Applying it needs a manager PIN. Per-discount so a track can wave through
        /// "Military 10%" while still gating "Employee 50%".</summary>
        public bool RequiresManager { get; set; }

        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        /// <summary>What this takes off a given amount, clamped so it can never exceed the amount
        /// (a $10-off discount on a $6 item takes $6) and never mints a negative. Mirrors
        /// SeasonPassBenefit.DiscountFor so the two behave identically at a counter.</summary>
        public int DiscountFor(int amountCents)
        {
            if (amountCents <= 0) return 0;
            var raw = Kind == "percent"
                ? (int)((long)amountCents * Value / 10_000L)
                : Value;
            return Math.Clamp(raw, 0, amountCents);
        }

        public bool AppliesTo(string surface) => Surfaces.Contains(surface);
    }

    /// <summary>The surfaces a discount can be scoped to. Mirrors the CHECK in Script0251 and the
    /// ledger's source_kind vocabulary; adding one means a migration and the code to honour it.</summary>
    public static class DiscountSurfaces
    {
        public const string EventTicket = "event_ticket";
        public const string Extras = "extras";
        public const string SeasonPass = "season_pass";
        public const string Membership = "membership";
        public const string Concession = "concession";
        public const string ShopSale = "shop_sale";
        public const string ShopRental = "shop_rental";

        public static readonly string[] All =
        {
            EventTicket, Extras, SeasonPass, Membership, Concession, ShopSale, ShopRental,
        };
    }
}
