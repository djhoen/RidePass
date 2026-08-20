namespace Services.Accounting
{
    /// <summary>
    /// The semantic account slots the QuickBooks sync posts to. Every tenant's chart of accounts is
    /// their own, so we never hardcode a QBO account id, the sync emits these keys and the tenant
    /// maps each one onto an account in their company from Admin → Settings → QuickBooks. The
    /// mapping lives in qbo_account_mapping (tenant_id, mapping_key).
    ///
    /// Keys are persisted in that table, so renaming one is a breaking change: it silently orphans
    /// every tenant's existing mapping and the key reads as unmapped. Add new keys; don't rename.
    /// </summary>
    public static class QboAccountKeys
    {
        // ── Revenue ──────────────────────────────────────────────────────────────────────
        public const string RevenueEventTicket = "revenue_event_ticket";
        /// <summary>
        /// A track's training department: lessons, camps and clinics. Not a source kind of its own,
        /// because every one of those is an ordinary `event` with tickets on it. It is selected per
        /// event type, by tenant_event_type.revenue_key (Script0274), which the read model carries
        /// out as revenue_key_override and <see cref="EffectiveRevenueKey"/> applies. Opt-in: an
        /// event type that names no key keeps posting to <see cref="RevenueEventTicket"/>.
        /// </summary>
        public const string RevenueTraining     = "revenue_training";
        public const string RevenueEventExtra  = "revenue_event_extra";
        public const string RevenueSeasonPass  = "revenue_season_pass";
        public const string RevenueMembership  = "revenue_membership";
        public const string RevenueRental      = "revenue_rental";
        public const string RevenueConcession  = "revenue_concession";
        /// <summary>
        /// Bike shop counter sales AND billed-out work orders. Deliberately one slot: a work order
        /// is billed as an ordinary shop_sale (BikeShopWorkOrderController.Bill), so the ledger has
        /// no way to tell labor from parts at the row level. Splitting them would need a flag the
        /// ledger does not carry.
        /// </summary>
        public const string RevenueBikeShop = "revenue_bike_shop";
        /// <summary>Bike shop rental fees. Separate from RevenueRental, which is the older, non-shop rental subsystem.</summary>
        public const string RevenueBikeShopRental = "revenue_bike_shop_rental";
        /// <summary>Rental deposit kept for damage on return, the liability becomes income.</summary>
        public const string RevenueDepositForfeited = "revenue_deposit_forfeited";
        /// <summary>Fallback for a sale kind added after this list. Keeps a day's post from failing.</summary>
        public const string RevenueOther = "revenue_other";

        // ── Liabilities ──────────────────────────────────────────────────────────────────
        /// <summary>
        /// Tax collected on behalf of a jurisdiction. The TENANT is merchant of record and remits it
        /// (TaxController). RidePass only calculates and collects. Never revenue.
        /// </summary>
        public const string LiabilitySalesTax = "liability_sales_tax";
        /// <summary>Tips collected for staff. Owed out, not earned.</summary>
        public const string LiabilityTips = "liability_tips";
        /// <summary>
        /// Unredeemed gift card value. Credited when a card is SOLD, debited as it's redeemed, which
        /// is what stops a gift card being booked as revenue twice (once on sale, once on redemption).
        /// </summary>
        public const string LiabilityGiftCard = "liability_gift_card";
        /// <summary>Refundable rental security deposits held against an outstanding rental.</summary>
        public const string LiabilityRentalDeposit = "liability_rental_deposit";

        // ── Assets ───────────────────────────────────────────────────────────────────────
        /// <summary>
        /// Platform-charge mode: money RidePass has collected and owes the tenant. Cleared when the
        /// monthly payout Transfer lands in their bank. The balance here should track the tenant's
        /// "available balance" on the Payouts screen.
        /// </summary>
        public const string AssetRidepassReceivable = "asset_ridepass_receivable";
        /// <summary>
        /// Direct-charge mode: funds sitting in the tenant's OWN Stripe balance (they're merchant of
        /// record). Their Stripe payout to bank clears this.
        /// </summary>
        public const string AssetStripeClearing = "asset_stripe_clearing";
        /// <summary>Cash tender taken at the counter, not yet deposited.</summary>
        public const string AssetUndepositedCash = "asset_undeposited_cash";

        // ── Expenses ─────────────────────────────────────────────────────────────────────
        /// <summary>Stripe's processing fee. Also carries chargeback fees.</summary>
        public const string ExpenseStripeFees = "expense_stripe_fees";
        /// <summary>RidePass's service charge, plus SMS and email campaign charges.</summary>
        public const string ExpenseRidepassFees = "expense_ridepass_fees";

        /// <summary>Every key, in the order the settings screen should render them.</summary>
        public static readonly string[] All =
        {
            RevenueEventTicket, RevenueTraining, RevenueEventExtra, RevenueSeasonPass, RevenueMembership,
            RevenueRental, RevenueConcession, RevenueBikeShop, RevenueBikeShopRental,
            RevenueDepositForfeited, RevenueOther,
            LiabilitySalesTax, LiabilityTips, LiabilityGiftCard, LiabilityRentalDeposit,
            AssetRidepassReceivable, AssetStripeClearing, AssetUndepositedCash,
            ExpenseStripeFees, ExpenseRidepassFees,
        };

        /// <summary>
        /// Revenue slot for a ledger source_kind. 'pass' is the removed day-pass subsystem
        /// (Script0118), kept only so historical rows, if any survive, still resolve.
        /// </summary>
        public static string RevenueForSourceKind(string? sourceKind) => sourceKind switch
        {
            "event_ticket" => RevenueEventTicket,
            "extras"       => RevenueEventExtra,
            "season_pass"  => RevenueSeasonPass,
            "membership"   => RevenueMembership,
            "rental"        => RevenueRental,
            "concession"    => RevenueConcession,
            "pass"          => RevenueEventTicket,
            // Damage kept out of a security deposit on return. A separate slot from RevenueRental so
            // a track can see damage income apart from what they actually earned renting the gear out.
            "rental_deposit" => RevenueDepositForfeited,
            "shop_sale"     => RevenueBikeShop,
            "shop_rental"   => RevenueBikeShopRental,
            // A work-order deposit is the same revenue stream as the job it belongs to, just
            // recognized when it is taken rather than at bill-out. There is no double count to
            // worry about: OnShopSalePaid books the bill-out as
            // total - deposit_applied - credit_applied (StripePurchaseFinalizer.cs:874), so the
            // deposit row and the remainder row sum to the job. Any deposit the job never used is
            // handed back as its own negative 'shop_wo_deposit' refund row.
            "shop_wo_deposit" => RevenueBikeShop,
            // Damage kept out of a bike-shop rental deposit on return. BikeShopRentalController
            // writes this row only for the amount actually CAPTURED from the hold ("Damage captured
            // from rental deposit"), so it is earned income, the same thing 'rental_deposit' is for
            // the older rental subsystem, and it shares that slot.
            "shop_rental_deposit" => RevenueDepositForfeited,
            _               => RevenueOther,
        };

        /// <summary>
        /// The revenue slot a sale actually posts to, once the tenant's own department mapping is
        /// taken into account. <paramref name="overrideKey"/> is v_accounting_entries'
        /// revenue_key_override, i.e. tenant_event_type.revenue_key for the event the row hangs
        /// off (Script0274); it is null for every row that has no event behind it.
        ///
        /// An override is honored only when it names a key this build knows about. That is not
        /// defensive noise: the column is written by a migration and, later, by an admin screen, so
        /// a key from a NEWER schema can reach an OLDER deployment mid-rollout. Falling back is
        /// then strictly better than passing it through, because an account slot no tenant has
        /// mapped blocks the whole day's journal entry from posting (QuickBooksController
        /// .RequiredKeys / MappingComplete), whereas the fallback books the day the way it was
        /// booked yesterday.
        /// </summary>
        public static string EffectiveRevenueKey(string? sourceKind, string? overrideKey) =>
            !string.IsNullOrEmpty(overrideKey) && All.Contains(overrideKey, StringComparer.Ordinal)
                ? overrideKey
                : RevenueForSourceKind(sourceKind);

        /// <summary>Human label for the settings screen. Mirrored in the Vue copy.</summary>
        public static string Label(string key) => key switch
        {
            RevenueEventTicket      => "Event ticket & gate revenue",
            RevenueTraining         => "Training Center revenue (lessons, camps, clinics)",
            RevenueEventExtra       => "Extras revenue (camping, parking, merch)",
            RevenueSeasonPass       => "Season pass revenue",
            RevenueMembership       => "Membership revenue",
            RevenueRental           => "Rental revenue",
            RevenueConcession       => "Concession / food & beverage revenue",
            RevenueBikeShop         => "Bike shop sales",
            RevenueBikeShopRental   => "Bike shop rentals",
            RevenueDepositForfeited => "Forfeited deposits (damage income)",
            RevenueOther            => "Other revenue",
            LiabilitySalesTax       => "Sales tax payable",
            LiabilityTips           => "Tips payable",
            LiabilityGiftCard       => "Gift card liability",
            LiabilityRentalDeposit  => "Rental deposits held",
            AssetRidepassReceivable => "RidePass receivable (undeposited funds)",
            AssetStripeClearing     => "Stripe clearing account",
            AssetUndepositedCash    => "Cash on hand",
            ExpenseStripeFees       => "Payment processing fees",
            ExpenseRidepassFees     => "RidePass service fees",
            _                       => key,
        };
    }
}
