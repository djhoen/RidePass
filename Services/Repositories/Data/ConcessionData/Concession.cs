namespace Services.Repositories.Data.ConcessionData
{
    public class ConcessionProduct
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public string Category { get; set; } = "other";   // legacy free-text; superseded by CategoryId
        // Tenant-defined category (replaces the old fixed enum). Name/SortOrder are joined for display.
        public Guid? CategoryId { get; set; }
        public string? CategoryName { get; set; }
        public int CategorySortOrder { get; set; }
        public int PriceCents { get; set; }
        public string? ImageUrl { get; set; }
        // Whether this item appears in the menu-board photo carousel (only shown when it has an image
        // and isn't sold out). Defaults true.
        public bool ShowInCarousel { get; set; } = true;
        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; }
        // Which kitchen station prepares this item (fryer/grill/...). NULL = default queue.
        public Guid? StationId { get; set; }
        // False = grab-and-go (bagged chips, canned soda): nothing to make, so it never shows on the
        // cook screen and its sale line is born 'ready'. Defaults true (made to order).
        public bool RequiresPrep { get; set; } = true;
        // Stock count for simple (no-variant) items. NULL = unlimited. Variant items track stock per variant.
        public int? Inventory { get; set; }
        // Business date this item is manually 86'd for; while == today (UTC) it's unavailable, then auto-clears.
        public DateTime? SoldOutDate { get; set; }
        // Whether this entree can be upgraded to a combo (shared tenant-level "make it a combo" definition).
        public bool ComboAvailable { get; set; }
        // Tax category that sets this item's rate. NULL = use the tenant's default category.
        public Guid? TaxCategoryId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    // A tenant-defined sales-tax rate bucket (e.g. "Prepared food" 8.25%, "Packaged" 2.9%, "Exempt" 0%).
    // RateBps is basis points (825 = 8.25%). One category per tenant is the default (used when an item
    // has no explicit category).
    public class ConcessionTaxCategory
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = null!;
        public int RateBps { get; set; }
        public bool IsDefault { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }
    }

    // A tenant-defined discount preset the POS shows as a one-tap button. Kind = 'percent' stores Value
    // in basis points (1000 = 10%); kind = 'amount' stores Value in cents. Presets are tenant-approved,
    // so a cashier can apply one without a manager PIN.
    public class ConcessionDiscountPreset
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = null!;
        public string Kind { get; set; } = "percent";   // 'percent' | 'amount'
        public int Value { get; set; }                   // bps when percent, cents when amount
        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    // A tenant-defined comp reason ("Rider comp", "Employee meal", "Manager comp"). DefaultKind = 'full'
    // comps the whole price; 'percent'/'amount' set a default partial value (bps / cents). Applying a comp
    // always requires a manager PIN and is logged for the void/comp report.
    public class ConcessionCompReason
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = null!;
        public string DefaultKind { get; set; } = "full";   // 'full' | 'percent' | 'amount'
        public int DefaultValue { get; set; }                // bps when percent, cents when amount; ignored for full
        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    // ── Combos (shared, tenant-level "make it a combo" definition) ───────────────
    // A size tier the customer picks (Regular/Large/XL...). PriceCents is the upcharge over the entree;
    // SizeLabel matches a component variant's size so the side/drink resolve to that size at this tier.
    public class ConcessionComboTier
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = null!;
        public string? SizeLabel { get; set; }
        public int PriceCents { get; set; }
        public int SortOrder { get; set; }
    }

    // A choose-one slot in the combo (Side, Drink). Tenant-level: shared by every combo-available item.
    public class ConcessionComboSlot
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = null!;
        public bool IsRequired { get; set; } = true;
        public int SortOrder { get; set; }
        // Hydrated for the editor / build modal; not a stored column on the slot.
        public List<ConcessionComboSlotOption> Options { get; set; } = new();
    }

    // A candidate component in a slot. The included (IsDefault) option is covered by the tier price;
    // others are charged the price difference at the chosen tier size. The size variant + price come
    // from the component product at sale time, so no per-option price is stored here.
    public class ConcessionComboSlotOption
    {
        public Guid Id { get; set; }
        public Guid SlotId { get; set; }
        public Guid ComponentProductId { get; set; }
        public bool IsDefault { get; set; }
        public int SortOrder { get; set; }
        // Joined for display / resolution.
        public string? ComponentName { get; set; }
        public Guid? StationId { get; set; }   // component's station, snapshotted onto the child line
    }

    // ── Inventory (ingredients / stockable goods) ───────────────────────────────
    public class ConcessionInventoryItem
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = null!;
        public string Unit { get; set; } = "each";
        public int CostCents { get; set; }        // cost per unit
        public decimal OnHand { get; set; }        // theoretical quantity on hand
        // When set and on_hand <= this, the item is low on stock. NULL = no low-stock tracking.
        public decimal? LowStockThreshold { get; set; }
        // Dedupes the manager alert: set when notified, cleared when restocked above the threshold.
        public DateTime? LowStockNotifiedAt { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    // A product's recipe line: one unit of the product consumes Quantity of the inventory item.
    public class ConcessionRecipeLine
    {
        public Guid ProductId { get; set; }
        public Guid InventoryItemId { get; set; }
        public decimal Quantity { get; set; }
        public string? ItemName { get; set; }   // joined for display
        public string? Unit { get; set; }       // joined for display
    }

    public class ConcessionInventoryCount
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid? CountedBy { get; set; }
        public string? Note { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class ConcessionInventoryCountLine
    {
        public Guid Id { get; set; }
        public Guid CountId { get; set; }
        public Guid InventoryItemId { get; set; }
        public string NameSnapshot { get; set; } = null!;
        public string UnitSnapshot { get; set; } = null!;
        public int UnitCostCents { get; set; }
        public decimal ExpectedQty { get; set; }
        public decimal CountedQty { get; set; }
    }

    // A tenant-defined menu category (Sandwiches, Burgers, Sides, ...) for grouping + display order.
    public class ConcessionCategory
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = null!;
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
        // Board this category appears on; null = every board (and the implicit single board for
        // tenants that never created named boards).
        public Guid? MenuBoardId { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    // A named in-venue menu board screen (one per TV). Tenants can create any number; categories
    // point at a board via MenuBoardId.
    public class ConcessionMenuBoard
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = null!;
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }
    }

    // A paired customer-facing display tablet for the POS. The POS pushes the in-progress order as
    // an opaque JSON snapshot (StateJson); the display polls it and writes back TipCents.
    public class ConcessionDisplay
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string PairCode { get; set; } = null!;
        public string? StateJson { get; set; }
        public int? TipCents { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    // Promo callout tile rotated through the menu board carousel ("Make it a combo $5.99").
    // Null MenuBoardId = every board; null ImageUrl = text tile on the accent color.
    public class ConcessionMenuPromo
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid? MenuBoardId { get; set; }
        public string Title { get; set; } = null!;
        public string? Subtitle { get; set; }
        public string? ImageUrl { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }
    }

    // Per-tenant styling for the in-venue menu board. Null colors/logo fall back to the tenant brand.
    public class ConcessionMenuSettings
    {
        public Guid TenantId { get; set; }
        public string? LogoUrl { get; set; }
        public string? BackgroundColor { get; set; }
        public string? TextColor { get; set; }
        public string? AccentColor { get; set; }
        public bool ShowCarousel { get; set; } = true;
        public int CarouselSeconds { get; set; } = 5;
        public bool TipsEnabled { get; set; }   // default off; tenant opts into tipping
        public bool CustomerDisplayEnabled { get; set; }   // POS flags an unpaired register when on
        public int PrepWarnMinutes { get; set; } = 5;    // cook screen ticket turns amber after this
        public int PrepLateMinutes { get; set; } = 10;   // ...and red after this
        // Weekly online-ordering hours as JSON (7 entries, Sun..Sat). NULL = always open. Evaluated in
        // the tenant's timezone.
        public string? OrderingHoursJson { get; set; }
        // Open-season date ranges as JSON ([{ startDate, endDate }], "yyyy-MM-dd", inclusive). NULL/empty
        // = open year-round; otherwise online ordering is closed outside every range. Tenant timezone.
        public string? OrderingSeasonsJson { get; set; }
        // When true (default), online ordering is closed on days with nothing on the events calendar, so
        // a closed track defaults to closed F&B.
        public bool RequireEventDay { get; set; } = true;
        // When true, item prices already include sales tax (tax is backed out for reporting). Default
        // false = tax is added on top of item prices at checkout.
        public bool PricesIncludeTax { get; set; }
        // Member-perk discounts at the F&B POS. When enabled, a verified Season Pass / LoamPass holder
        // gets the configured discount (Kind 'percent' = bps in Value, 'amount' = cents in Value). The
        // LoamPass perk does NOT consume a LoamPass admission credit. Off by default.
        public bool SeasonPassDiscountEnabled { get; set; }
        public string SeasonPassDiscountKind { get; set; } = "percent";
        public int SeasonPassDiscountValue { get; set; }
        public bool LoampassDiscountEnabled { get; set; }
        public string LoampassDiscountKind { get; set; } = "percent";
        public int LoampassDiscountValue { get; set; }
        // When true (default), an arbitrary manual percent/dollar discount needs a manager PIN. Presets
        // and member perks are pre-approved and never need one; comps always do regardless of this flag.
        public bool RequireManagerForManualDiscount { get; set; } = true;
        // When the tenant first loaded the starter catalog; NULL = never. Hides the "Load starter content"
        // button once used.
        public DateTime? SeededAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    // Per-tenant online-order throttle + quote-time config. Default disabled (no cap, quotes shown).
    // OnlinePaused is the manual staff pause; the auto-pause is computed live from the active queue.
    public class ConcessionOrderingCapacity
    {
        public Guid TenantId { get; set; }
        public bool CapacityEnabled { get; set; }
        public int BasePrepMinutes { get; set; } = 10;
        public int MaxActiveOrders { get; set; }      // 0 = no cap
        public bool ShowQuoteTimes { get; set; } = true;
        public bool OnlinePaused { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    // A kitchen station the cook screen can be split by (e.g. Fryer, Grill, Drinks).
    public class ConcessionStation
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = null!;
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }
    }

    // A kitchen ticket printer. Deliberately separate from ConcessionStation: stations split the
    // cook SCREENS, and printers do not have to follow that split (the common setup is grill and
    // fryer screens feeding one printer at the pass).
    public class ConcessionPrinter
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = null!;

        // ePOS-Print endpoint, e.g. https://192.168.1.50. Must be https - the POS is served over
        // https and browsers block mixed content, so a plain http printer fails silently.
        public string Url { get; set; } = null!;
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }

        // Stations this printer is scoped to. EMPTY = prints the whole order, which is also what
        // catches lines whose product has no station assigned.
        public List<Guid> StationIds { get; set; } = new();
    }

    // A structured modifier group (e.g. "Choose a side", "Add-ons"). min/max bound the selection.
    public class ConcessionModifierGroup
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = null!;
        public int MinSelect { get; set; }
        public int? MaxSelect { get; set; }   // null = unlimited
        public bool IsRequired { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }
    }

    public class ConcessionModifierOption
    {
        public Guid Id { get; set; }
        public Guid GroupId { get; set; }
        public string Name { get; set; } = null!;
        public int PriceDeltaCents { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class ConcessionVariant
    {
        public Guid Id { get; set; }
        public Guid ProductId { get; set; }
        public string? Size { get; set; }
        public string? Color { get; set; }
        public int? PriceCents { get; set; }   // null = use product price
        public string? ImageUrl { get; set; }
        public int? Inventory { get; set; }     // null = unlimited
        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class ConcessionSale
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Status { get; set; } = "pending";   // pending | paid | failed | refunded
        // Kitchen/pickup lifecycle, independent of payment status: active -> ready -> completed.
        public string FulfillmentStatus { get; set; } = "active";
        // Per-tenant, per-day pickup number called out to the customer. Assigned on payment success.
        public int? OrderNumber { get; set; }
        public int SubtotalCents { get; set; }
        public int TipCents { get; set; }
        // Total sales tax on the order (snapshot). When PricesIncludeTax, this is the portion baked into
        // SubtotalCents; otherwise it's added on top to reach TotalCents.
        public int TaxCents { get; set; }
        // Whether the item prices were tax-inclusive at sale time (so receipts label tax correctly).
        public bool PricesIncludeTax { get; set; }
        // Discount snapshot. SubtotalCents stays GROSS (pre-discount); DiscountCents is the total knocked
        // off; TaxCents/TotalCents already reflect the net. DiscountKind/Label describe what was applied;
        // Comp* + AuthorizedBy* capture the manager-approved comp trail for the void/comp report.
        public int DiscountCents { get; set; }
        public string? DiscountKind { get; set; }   // 'preset'|'percent'|'amount'|'comp'|'season_pass'|'loampass'|'mixed'
        public string? DiscountLabel { get; set; }
        public Guid? CompReasonId { get; set; }
        public string? CompReasonLabel { get; set; }
        public Guid? AuthorizedByUserId { get; set; }
        public string? AuthorizedByName { get; set; }
        public int TotalCents { get; set; }
        public string PaymentMethod { get; set; } = "stripe";   // stripe | stripe_direct | cash
        public string? StripePaymentIntentId { get; set; }
        // Connected account a direct card sale was charged on (NULL = platform/cash).
        public string? StripeConnectedAccountId { get; set; }
        public Guid? SoldByUserId { get; set; }
        // 'counter' (anonymous in-venue sale) or 'online' (a logged-in rider ordered from the web app).
        public string OrderChannel { get; set; } = "counter";
        public Guid? PurchaserUserId { get; set; }
        public string? PurchaserEmail { get; set; }
        public string? PurchaserName { get; set; }
        public bool IsRush { get; set; }   // cook-screen priority flag
        // Store credit spent as a tender on this order (Script0194): total_cents stays the full
        // value, the money paths (cash / card-present PI / ledger) collect total minus this.
        public int CreditAppliedCents { get; set; }
        public Guid? CreditAccountId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? PaidAt { get; set; }
    }

    public class ConcessionSaleLine
    {
        public Guid Id { get; set; }
        public Guid SaleId { get; set; }
        public Guid? ProductId { get; set; }
        public Guid? VariantId { get; set; }
        // Station this line is prepared at (snapshot from the product). NULL = default queue.
        public Guid? StationId { get; set; }
        // Snapshot of the product's grab-and-go setting. False = the cook screen never lists this line
        // and it is written with PrepStatus 'ready', so it can't hold the order in "Preparing".
        public bool RequiresPrep { get; set; } = true;
        public string NameSnapshot { get; set; } = null!;
        public string? VariantLabel { get; set; }
        public int UnitPriceCents { get; set; }
        public int Quantity { get; set; }
        // NET line total (after any line discount + allocated order discount). DiscountCents is what was
        // taken off this line's gross; DiscountKind/Label describe it for the receipt.
        public int LineTotalCents { get; set; }
        public int DiscountCents { get; set; }
        public string? DiscountKind { get; set; }
        public string? DiscountLabel { get; set; }
        // Tax snapshot for this line: the rate applied (basis points) and the tax amount. For inclusive
        // pricing the tax is the portion already inside LineTotalCents; otherwise it's added on top.
        public int TaxCents { get; set; }
        public int TaxRateBps { get; set; }
        // Kitchen prep state for this line: queued -> in_progress -> ready.
        public string PrepStatus { get; set; } = "queued";
        public string? Notes { get; set; }
        // Combo linkage: a combo parent line (IsCombo = true) is the entree sold as a combo; its side/
        // drink child lines reference it via ParentLineId and are priced $0. ComboTier snapshots the
        // chosen size tier name (e.g. "Large") for receipts / the cook screen.
        public Guid? ParentLineId { get; set; }
        public bool IsCombo { get; set; }
        public string? ComboTier { get; set; }
        // Hydrated for the kitchen/receipt views; not a stored column on the line.
        public List<ConcessionSaleLineModifier> Modifiers { get; set; } = new();
        // Transient: a combo parent's resolved child lines, persisted together by CreateSaleLines.
        public List<ConcessionSaleLine> Children { get; set; } = new();
    }

    // A frozen modifier selection on a sale line (snapshots so catalog edits don't rewrite history).
    public class ConcessionSaleLineModifier
    {
        public Guid Id { get; set; }
        public Guid SaleLineId { get; set; }
        public Guid? ModifierOptionId { get; set; }
        public string GroupNameSnapshot { get; set; } = null!;
        public string OptionNameSnapshot { get; set; } = null!;
        public int PriceDeltaCentsSnapshot { get; set; }
    }

    /// <summary>
    /// A pending concession sale the reconciler just swept (walk-off / cancelled reader). Carries the
    /// store credit that was applied at ring-up so the sweep can hand it back.
    /// </summary>
    public class StalePendingSale
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public int CreditAppliedCents { get; set; }
    }
}
