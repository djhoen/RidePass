namespace Services.Accounting
{
    /// <summary>
    /// Rolls the fine-grained revenue slots in <see cref="QboAccountKeys"/> up into the handful of
    /// BUSINESS UNITS an owner actually thinks in.
    ///
    /// The slots exist to be mapped onto a chart of accounts, so they are deliberately as granular
    /// as the ledger can distinguish: counter sales and rentals are separate slots inside the bike
    /// shop, forfeited deposits are separate from both. That granularity is right for QuickBooks
    /// and wrong for the question "which side of my business made money this month", which is what
    /// the Revenue by Department report answers. So the grouping lives here, once, instead of being
    /// re-invented in the report, the CSV and the Vue view.
    ///
    /// The names are generic on purpose. Every track has a gate, most have food, some have a shop
    /// and some have a training program, and the report has to read sensibly at all of them. A
    /// department with no activity in the period is simply not rendered, which is how a track with
    /// no bike shop never sees a bike shop heading.
    ///
    /// This is a STATIC mapping, not a configurable one. Making it per-tenant would need a table, a
    /// UI and a migration to earn its keep, and the one axis a track genuinely needs to control,
    /// which of their event types is training rather than gate revenue, is already controlled:
    /// tenant_event_type.revenue_key (Script0274) decides whether a ticket resolves to
    /// revenue_training or revenue_event_ticket long before it reaches this table. Extras follow
    /// the same rule: a lunch add-on bought against a camp resolves to revenue_training and lands
    /// under Training Center, while a t-shirt sold at the counter stays revenue_event_extra and
    /// sits with tickets and passes, because nothing about it says otherwise.
    /// </summary>
    public static class QboDepartments
    {
        public const string BikeShop        = "bike_shop";
        public const string FoodAndBeverage = "food_beverage";
        public const string TicketsPasses   = "tickets_passes";
        public const string Training        = "training";
        public const string Other           = "other";

        /// <summary>Departments in report order. Every revenue key resolves into exactly one.</summary>
        public static readonly string[] All =
        {
            TicketsPasses, Training, FoodAndBeverage, BikeShop, Other,
        };

        public static string Label(string department) => department switch
        {
            TicketsPasses   => "Tickets & Passes",
            Training        => "Training Center",
            FoodAndBeverage => "Food & Beverage",
            BikeShop        => "Bike Shop",
            _               => "Other",
        };

        /// <summary>
        /// The business unit a QuickBooks revenue slot belongs to. Anything unrecognised, including
        /// a key added by a newer build, falls into Other rather than being dropped: a report that
        /// silently loses a revenue line is worse than one with an unhelpful heading on it.
        ///
        /// Forfeited deposits sit under the bike shop because that is where nearly all of them come
        /// from in practice (the shop's rental fleet), and the older non-shop rental subsystem, the
        /// only other writer, has no department of its own to claim them.
        /// </summary>
        public static string ForRevenueKey(string? revenueKey) => revenueKey switch
        {
            QboAccountKeys.RevenueEventTicket      => TicketsPasses,
            QboAccountKeys.RevenueEventExtra       => TicketsPasses,
            QboAccountKeys.RevenueSeasonPass       => TicketsPasses,
            QboAccountKeys.RevenueMembership       => TicketsPasses,
            QboAccountKeys.RevenueTraining         => Training,
            QboAccountKeys.RevenueConcession       => FoodAndBeverage,
            QboAccountKeys.RevenueBikeShop         => BikeShop,
            QboAccountKeys.RevenueBikeShopRental   => BikeShop,
            QboAccountKeys.RevenueDepositForfeited => BikeShop,
            // The older, non-shop rental subsystem. Its own thing at the tracks that use it, but it
            // is not one of the units this report names, so it rolls up with everything else.
            QboAccountKeys.RevenueRental           => Other,
            _                                      => Other,
        };
    }
}
