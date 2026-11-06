namespace Services.Repositories.Data.BikeShopData
{
    // Read model behind the bike shop "Today" sheet: every rental and reservation for one tenant-
    // local business day, grouped by the program it was booked through (the shop's own rentals,
    // an event type such as Camp or After School, or a package such as Find Your Ride).
    //
    // Two layers live here. The *Row classes are what the four SQL statements in ShopDayRepository
    // hand back, one statement per concern (rentals for the day, their lines, their package links,
    // the events they hang off). ShopDayMath folds those into the ShopDaySheet the controller
    // returns; keeping the fold pure means the phase and program rules are unit-tested instead of
    // discovered at the counter.

    // ── Raw rows (one per SQL statement) ─────────────────────────────────────

    public class ShopDayRentalRow
    {
        public Guid Id { get; set; }
        public Guid? RenterUserId { get; set; }
        public string? RenterName { get; set; }
        public string? RenterEmail { get; set; }
        public string? RenterPhone { get; set; }
        public DateTime StartsAt { get; set; }
        public DateTime EndsAt { get; set; }
        public string Status { get; set; } = "pending";
        public DateTime? CheckedOutAt { get; set; }
        public DateTime? ReturnedAt { get; set; }
        public int? OrderNumber { get; set; }
        /// <summary>NULL means the rental was booked online; a user id means a staff member sold it.</summary>
        public Guid? SoldByUserId { get; set; }
        public string PaymentMethod { get; set; } = "stripe";
        public Guid? EventId { get; set; }
        public int TotalCents { get; set; }
        public int DepositCents { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class ShopDayLineRow
    {
        public Guid RentalId { get; set; }
        public string NameSnapshot { get; set; } = null!;
        public string? VariantLabel { get; set; }
        public int Quantity { get; set; }
        public Guid? ItemId { get; set; }
        /// <summary>Serialized unit's own name ("Trek Fuel #3"); null for a pool line or an unassigned unit.</summary>
        public string? UnitLabel { get; set; }
        public string? UnitSerial { get; set; }
        /// <summary>The product's category, which the sheet calls a product type.</summary>
        public Guid? ProductTypeId { get; set; }
        public string? ProductTypeName { get; set; }
    }

    public class ShopDayPackageLinkRow
    {
        public Guid RentalId { get; set; }
        public Guid PackageId { get; set; }
        public string PackageName { get; set; } = null!;
        public DateTime? SessionStartAt { get; set; }
        public string? BuyerName { get; set; }
    }

    public class ShopDayEventRow
    {
        public Guid EventId { get; set; }
        public string Title { get; set; } = null!;
        public DateTime StartsAt { get; set; }
        public Guid EventTypeId { get; set; }
        public string EventTypeName { get; set; } = null!;
        public string EventTypeCode { get; set; } = null!;
        public string? Color { get; set; }
        public int SortOrder { get; set; }
    }

    /// <summary>Everything the repository reads for one day, before any grouping.</summary>
    public class ShopDayRaw
    {
        public List<ShopDayRentalRow> Rentals { get; set; } = new();
        public List<ShopDayLineRow> Lines { get; set; } = new();
        public List<ShopDayPackageLinkRow> PackageLinks { get; set; } = new();
        public List<ShopDayEventRow> Events { get; set; } = new();
    }

    // ── The sheet (what GET BikeShopDay returns) ──────────────────────────────

    public class ShopDaySheet
    {
        /// <summary>The tenant-local calendar date the sheet is for, yyyy-MM-dd.</summary>
        public string Date { get; set; } = null!;
        /// <summary>IANA zone the day boundaries were computed in, so the client formats times the same way.</summary>
        public string Timezone { get; set; } = "UTC";
        public ShopDayCounts Totals { get; set; } = new();
        public List<ShopDayGroup> Groups { get; set; } = new();
        /// <summary>Every program that has rows on this day, in group order, for the filter chips.</summary>
        public List<ShopDayProgram> Programs { get; set; } = new();
        /// <summary>Product categories present in the day's lines, for the filter.</summary>
        public List<ShopDayProductType> ProductTypes { get; set; } = new();
    }

    public class ShopDayCounts
    {
        public int PickupsDue { get; set; }
        public int OutNow { get; set; }
        public int ReturnsDue { get; set; }
        public int Reserved { get; set; }
        public int Unpaid { get; set; }
        public int Returned { get; set; }
    }

    public class ShopDayGroup
    {
        /// <summary>shop_rental | event_type:{guid} | package:{guid}</summary>
        public string Key { get; set; } = null!;
        /// <summary>shop_rental | event_type | package</summary>
        public string Kind { get; set; } = null!;
        public string Label { get; set; } = null!;
        /// <summary>The event type's color; null for the shop's own rentals and for packages.</summary>
        public string? Color { get; set; }
        /// <summary>Position in the sheet: Rentals first, then event types by their sort order, then packages by name.</summary>
        public int SortOrder { get; set; }
        public ShopDayCounts Counts { get; set; } = new();
        public List<ShopDayRow> Rows { get; set; } = new();
    }

    public class ShopDayRow
    {
        public Guid RentalId { get; set; }
        public int? OrderNumber { get; set; }
        public string Status { get; set; } = null!;
        public DateTime StartsAtUtc { get; set; }
        public DateTime EndsAtUtc { get; set; }
        public DateTime? CheckedOutAtUtc { get; set; }
        public DateTime? ReturnedAtUtc { get; set; }
        public string? CustomerName { get; set; }
        public string? CustomerPhone { get; set; }
        public string? CustomerEmail { get; set; }
        public Guid? CustomerUserId { get; set; }
        public List<ShopDayItem> Items { get; set; } = new();
        /// <summary>counter | online</summary>
        public string Channel { get; set; } = "counter";
        public string? EventTitle { get; set; }
        public DateTime? EventStartsAtUtc { get; set; }
        public string? PackageName { get; set; }
        public DateTime? SessionStartAtUtc { get; set; }
        public int TotalCents { get; set; }
        public int DepositCents { get; set; }
        /// <summary>What is still owed: the whole total while pending, nothing once paid.</summary>
        public int BalanceDueCents { get; set; }
        /// <summary>pickup | out | return | reserved | done | cancelled | unpaid (see ShopDayMath.PhaseFor).</summary>
        public string Phase { get; set; } = null!;
        /// <summary>The row is behind: a return that was due before this day began, or a pickup
        /// that should have happened before it (a no-show still holding its reservation).</summary>
        public bool Overdue { get; set; }
    }

    public class ShopDayItem
    {
        public string Name { get; set; } = null!;
        public string? VariantLabel { get; set; }
        public int Quantity { get; set; }
        public string? UnitLabel { get; set; }
        public string? UnitSerial { get; set; }
        /// <summary>The line's product category (what the sheet's filter calls a product type), so
        /// the client can match rows to a chosen type. Null when the variant no longer resolves.</summary>
        public Guid? CategoryId { get; set; }
        public string? CategoryName { get; set; }
    }

    public class ShopDayProgram
    {
        public string Key { get; set; } = null!;
        public string Label { get; set; } = null!;
        public string Kind { get; set; } = null!;
    }

    public class ShopDayProductType
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
    }
}
