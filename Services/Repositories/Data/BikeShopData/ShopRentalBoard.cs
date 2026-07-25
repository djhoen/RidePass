namespace Services.Repositories.Data.BikeShopData
{
    // Read model behind the Rental Board: everything one screen needs to draw a timeline of the
    // rental fleet for a window, in a single round trip.
    //
    // Why a dedicated model rather than reusing the per-variant availability probe: the probe
    // (GetPoolAvailability / GetFreeSerializedUnits) answers "how many are free across this whole
    // window" with one scalar per variant. A timeline needs the opposite shape: the individual
    // reservations laid out in time, per physical unit, for the whole fleet at once. N probes
    // returning N scalars can't draw it.
    //
    // The payload is deliberately SELF-CONTAINED (resources carry their own rates, deposits, and
    // category names). BikeShop/Categories and BikeShop/Products sit behind CatalogManage, but the
    // board is a ShopCounter screen; folding what it needs into this one response keeps a
    // counter-only user off those endpoints entirely.

    public class ShopRentalBoard
    {
        /// <summary>Echo of the requested window, so the client never draws against a window that
        /// drifted from the one the data was computed for.</summary>
        public DateTime StartsAt { get; set; }
        public DateTime EndsAt { get; set; }
        /// <summary>One row per bookable thing: a serialized unit, or a pool variant with capacity.</summary>
        public List<ShopRentalBoardResource> Resources { get; set; } = new();
        /// <summary>Every active reservation overlapping the window.</summary>
        public List<ShopRentalBoardSegment> Segments { get; set; } = new();
        /// <summary>The categories actually present in the rentable fleet, for the filter. Computed
        /// over the UNFILTERED fleet so choosing one category never empties the picker.</summary>
        public List<ShopRentalBoardCategory> Categories { get; set; } = new();
    }

    /// <summary>
    /// A timeline row. Serialized resources are one physical unit each (ItemId set, Capacity 1);
    /// pool resources are one row for the whole bucket (ItemId null, Capacity = fleet size).
    /// </summary>
    public class ShopRentalBoardResource
    {
        /// <summary>Stable row key: the item id for a serialized unit, the variant id for a pool.</summary>
        public Guid Id { get; set; }
        public Guid VariantId { get; set; }
        public Guid? ItemId { get; set; }
        public string TrackingKind { get; set; } = "pool";   // pool|serialized
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = null!;
        public string? Brand { get; set; }
        public Guid? CategoryId { get; set; }
        public string? CategoryName { get; set; }
        public string? Size { get; set; }
        public string? Color { get; set; }
        public string? Gender { get; set; }
        public string? Sku { get; set; }
        /// <summary>Serialized only: the unit's own name ("Trek Fuel #3").</summary>
        public string? UnitLabel { get; set; }
        public string? Serial { get; set; }
        /// <summary>Serialized only: the shop_item status. 'maintenance' means the unit is on the
        /// bench and must not be bookable, even though nothing is reserved against it.</summary>
        public string? ItemStatus { get; set; }
        /// <summary>How many can be out at once. 1 for a serialized unit; the fleet total for a pool.</summary>
        public int Capacity { get; set; }
        public int DailyRateCents { get; set; }
        public int DepositCents { get; set; }
    }

    /// <summary>One rental line drawn as a bar. Windows come from the rental, not the line: a
    /// booking reserves all of its gear for the same window.</summary>
    public class ShopRentalBoardSegment
    {
        public Guid RentalId { get; set; }
        public Guid LineId { get; set; }
        public Guid VariantId { get; set; }
        public Guid? ItemId { get; set; }
        public int Quantity { get; set; }
        public DateTime StartsAt { get; set; }
        public DateTime EndsAt { get; set; }
        public string Status { get; set; } = null!;         // pending|paid|out
        public string? RenterName { get; set; }
        public string? RenterEmail { get; set; }
        public int? OrderNumber { get; set; }
        public DateTime? CheckedOutAt { get; set; }
        public string NameSnapshot { get; set; } = null!;
        public string? VariantLabel { get; set; }
    }

    public class ShopRentalBoardCategory
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
    }
}
