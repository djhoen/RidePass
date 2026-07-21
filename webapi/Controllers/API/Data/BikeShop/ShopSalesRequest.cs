using Services.Repositories.Data.BikeShopData;

namespace webapi.Controllers.API.Data.BikeShop
{
    /// <summary>
    /// Query-string filters for the shop sales list. Bound as an object rather than a dozen loose
    /// [FromQuery] parameters because the multi-selects (status, payment method) arrive as repeated
    /// keys and the set keeps growing.
    /// </summary>
    public class ShopSalesRequest
    {
        /// <summary>Order number, customer name or email, or an item name on the sale.</summary>
        public string? Search { get; set; }

        public DateTime? From { get; set; }
        public DateTime? To { get; set; }

        /// <summary>Repeated key: ?status=paid&amp;status=refunded. Empty means every status.</summary>
        public List<string>? Status { get; set; }
        /// <summary>Repeated key: ?paymentMethod=cash&amp;paymentMethod=stripe.</summary>
        public List<string>? PaymentMethod { get; set; }
        /// <summary>counter | online. Null means both.</summary>
        public string? Channel { get; set; }

        public bool AwaitingPickupOnly { get; set; }
        public bool WorkOrderOnly { get; set; }
        public Guid? SoldByUserId { get; set; }

        public string? SortBy { get; set; }
        public bool SortDesc { get; set; } = true;

        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 25;

        /// <summary>
        /// Values are whitelisted here rather than trusted: an unrecognised status or payment
        /// method is dropped, so a typo returns everything instead of silently returning nothing.
        /// </summary>
        public ShopSaleQuery ToQuery() => new()
        {
            Search = Search,
            From = From,
            To = To,
            Statuses = Filter(Status, ValidStatuses),
            PaymentMethods = Filter(PaymentMethod, ValidPaymentMethods),
            Channel = ValidChannels.Contains(Channel ?? "") ? Channel : null,
            AwaitingPickupOnly = AwaitingPickupOnly,
            WorkOrderOnly = WorkOrderOnly,
            SoldByUserId = SoldByUserId,
            SortBy = SortBy,
            SortDesc = SortDesc,
            Page = Page,
            PageSize = PageSize,
        };

        private static readonly HashSet<string> ValidStatuses =
            new(StringComparer.OrdinalIgnoreCase) { "pending", "paid", "failed", "refunded" };
        private static readonly HashSet<string> ValidPaymentMethods =
            new(StringComparer.OrdinalIgnoreCase) { "stripe", "stripe_direct", "cash", "voucher" };
        private static readonly HashSet<string> ValidChannels =
            new(StringComparer.OrdinalIgnoreCase) { "counter", "online" };

        private static List<string>? Filter(List<string>? values, HashSet<string> allowed)
        {
            if (values is null || values.Count == 0) return null;
            // Lowercased to match how the column stores them; the DB comparison is exact.
            var kept = values.Where(v => allowed.Contains(v)).Select(v => v.ToLowerInvariant()).Distinct().ToList();
            return kept.Count == 0 ? null : kept;
        }
    }
}
