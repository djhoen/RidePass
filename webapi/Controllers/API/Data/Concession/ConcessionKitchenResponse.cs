namespace webapi.Controllers.API.Data.Concession
{
    // A paid, not-yet-completed order for the cook screen: the called-out number plus its lines,
    // each with prep status, station, notes, and the selected modifiers the cook needs to see.
    public class ConcessionKitchenResponse
    {
        public List<KitchenOrder> Orders { get; set; } = new();
        public KitchenStats Stats { get; set; } = new();
        // Tenant's color-escalation targets so the cook screen colors match the configured thresholds.
        public int WarnMinutes { get; set; } = 5;
        public int LateMinutes { get; set; } = 10;

        // Today's prep performance (since local midnight in the tenant's timezone).
        public class KitchenStats
        {
            public int CompletedToday { get; set; }
            public int AvgPrepMinutes { get; set; }   // submitted -> completed, averaged
        }

        public class KitchenOrder
        {
            public Guid SaleId { get; set; }
            public int? OrderNumber { get; set; }
            public string FulfillmentStatus { get; set; } = "active";
            // Buyer's name for online orders (from their account); null for anonymous counter sales.
            public string? CustomerName { get; set; }
            public bool IsRush { get; set; }
            // When the order entered the kitchen queue (i.e. when it was paid), so the cook-screen age
            // timer reflects real prep wait rather than when the cart was first opened.
            public DateTime QueuedAtUtc { get; set; }
            public List<KitchenLine> Lines { get; set; } = new();
        }

        public class KitchenLine
        {
            public Guid LineId { get; set; }
            public Guid? StationId { get; set; }
            public string Name { get; set; } = null!;
            public string? VariantLabel { get; set; }
            public int Quantity { get; set; }
            public string PrepStatus { get; set; } = "queued";
            public string? Notes { get; set; }
            // Added = non-default options the customer chose; Removed = standard defaults they took off;
            // Standard = the default options that stayed (hidden by default on the cook screen, shown when
            // the cook toggles defaults on).
            public List<string> Added { get; set; } = new();
            public List<string> Removed { get; set; } = new();
            public List<string> Standard { get; set; } = new();
            // Combo linkage: IsCombo marks the entree sold as a combo (with ComboTier, e.g. "Large");
            // its side/drink lines carry ParentLineId.
            public bool IsCombo { get; set; }
            public Guid? ParentLineId { get; set; }
            public string? ComboTier { get; set; }
        }
    }
}
