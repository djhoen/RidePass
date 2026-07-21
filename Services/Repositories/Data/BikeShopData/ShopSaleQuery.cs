namespace Services.Repositories.Data.BikeShopData
{
    /// <summary>
    /// Filter/paging criteria for the shop sales list. Sales only ever accumulate, so the screen is
    /// queried as a filtered page rather than a "most recent N" slice — otherwise searching for a
    /// customer reports "no results" purely because their sale fell off the end of the list.
    /// </summary>
    public class ShopSaleQuery
    {
        /// <summary>
        /// Free text, matched case-insensitively against order number, buyer name and email, and
        /// the item names on the sale. One box, because staff look up a sale by whatever the
        /// customer happens to remember.
        /// </summary>
        public string? Search { get; set; }

        /// <summary>Inclusive lower bound on the sale date.</summary>
        public DateTime? From { get; set; }
        /// <summary>Inclusive upper bound: the whole of this day counts (handled as &lt; day + 1).</summary>
        public DateTime? To { get; set; }

        /// <summary>pending | paid | failed | refunded. Empty means every status.</summary>
        public List<string>? Statuses { get; set; }
        /// <summary>stripe | stripe_direct | cash | voucher. Empty means every method.</summary>
        public List<string>? PaymentMethods { get; set; }
        /// <summary>counter | online. Null means both.</summary>
        public string? Channel { get; set; }

        /// <summary>Only paid online orders the customer has not collected yet.</summary>
        public bool AwaitingPickupOnly { get; set; }
        /// <summary>Only sales attached to a repair work order.</summary>
        public bool WorkOrderOnly { get; set; }

        public Guid? SoldByUserId { get; set; }

        /// <summary>createdAt | orderNumber | total | buyer. Anything else falls back to createdAt.</summary>
        public string? SortBy { get; set; }
        public bool SortDesc { get; set; } = true;

        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 25;
    }

    /// <summary>One page of sales plus the totals for the WHOLE filtered set.</summary>
    public class ShopSalesPage
    {
        public List<ShopSaleWithLines> Rows { get; set; } = new();
        /// <summary>Matching sales across all pages.</summary>
        public int Total { get; set; }
        public ShopSalesTotals Totals { get; set; } = new();
        /// <summary>
        /// Paid online orders not yet collected, across the whole tenant and deliberately ignoring
        /// the filters: it is a work-queue badge ("someone is waiting"), not a property of the
        /// current view. Filtering it would hide the queue exactly when it is being worked.
        /// </summary>
        public int AwaitingPickupCount { get; set; }
    }

    /// <summary>
    /// Money totals for the filtered set, not the visible page: "what did we take last week" is a
    /// question about the filter, not about the 25 rows on screen.
    /// </summary>
    public class ShopSalesTotals
    {
        /// <summary>Sum of paid sales. bigint because a year of sales overflows int cents.</summary>
        public long PaidCents { get; set; }
        /// <summary>Sum of sales that were refunded. Reported alongside rather than netted off, so
        /// a busy day that was half refunded cannot masquerade as a quiet one.</summary>
        public long RefundedCents { get; set; }
        /// <summary>Tax collected on the paid sales.</summary>
        public long TaxCents { get; set; }
        public int PaidCount { get; set; }
        public int RefundedCount { get; set; }
    }
}
