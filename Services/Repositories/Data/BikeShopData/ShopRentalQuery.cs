namespace Services.Repositories.Data.BikeShopData
{
    /// <summary>
    /// Which slice of the booking history a rentals list wants. The counter's normal question is
    /// "what is still coming", but a shop also has to look back (who had that bike in June, what
    /// did we take last month), and the two want opposite sort orders.
    /// </summary>
    public enum ShopRentalScope
    {
        /// <summary>Not finished yet: the window has not closed. Sorted soonest-first.</summary>
        Upcoming = 1,
        /// <summary>The window has closed. Sorted most-recent-first.</summary>
        Past = 2,
        /// <summary>Everything, most-recent-first.</summary>
        All = 3,
    }

    /// <summary>
    /// Filter/paging criteria for the All Bookings list. Unlike the fleet (tens of rows), booking
    /// history grows forever, so this screen is queried as a page rather than loaded whole.
    /// </summary>
    public class ShopRentalQuery
    {
        public ShopRentalScope Scope { get; set; } = ShopRentalScope.Upcoming;

        /// <summary>
        /// Free text, matched case-insensitively against the renter's name and email, and matched
        /// exactly against the order number when the text is a number. One box, because staff
        /// looking someone up have either a name or the number off their receipt.
        /// </summary>
        public string? Search { get; set; }

        /// <summary>Empty = every status. Values are shop_rental.status.</summary>
        public List<string> Statuses { get; set; } = new();

        /// <summary>
        /// Explicit window on the rental's own dates, applied as an OVERLAP: a booking counts when
        /// any part of it falls inside [FromUtc, ToUtc). A rental that starts before the range and
        /// runs into it is exactly the sort of thing a date filter must not hide.
        /// </summary>
        public DateTime? FromUtc { get; set; }
        public DateTime? ToUtc { get; set; }

        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 25;
    }

    /// <summary>One page of bookings plus the total matching count, for the pager.</summary>
    public class ShopRentalPage
    {
        public List<ShopRentalWithLines> Rows { get; set; } = new();
        public int Total { get; set; }
    }
}
