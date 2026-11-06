using Services.Payments;
using Services.Repositories.Data.BikeShopData;
using Services.Repositories.Interfaces;

namespace Services.BikeShop
{
    /// <summary>What releasing a listing's online reservation found.</summary>
    public enum ReservationRelease
    {
        /// <summary>The unit is free: the checkout is dead, its sale line no longer names the unit, the hold is cleared.</summary>
        Released,
        /// <summary>The checkout actually paid (or is paid): the unit is sold, not free. Nothing was cleared.</summary>
        AlreadyPaid,
        /// <summary>Stripe could not confirm the PaymentIntent is dead; nothing is known to be safe. Nothing was cleared.</summary>
        Blocked,
    }

    /// <summary>
    /// The ONE way a listing's online reservation is ever cleared. Whoever takes a unit back from a
    /// checkout (a new buyer over a stale hold, the register, Unlist, a re-list over a stranded row,
    /// or the checkout itself when its PaymentIntent could not start) goes through here, and the
    /// caller decides beforehand whether a live hold may be released at all.
    ///
    /// Nulling reserved_sale_id is not enough to sell the unit again: the checkout's shop_sale_line
    /// still names the unit, and uk_shop_sale_line_item is unique over every line that names one,
    /// paid or not. So the sale is made permanently unpayable (its PaymentIntent cancelled at Stripe,
    /// the row marked dead), its line detached from the unit, and only then the hold cleared. If
    /// Stripe reports the intent already succeeded, the buyer paid in the race window and the unit
    /// is theirs: the caller must refuse, never overwrite.
    ///
    /// Callers hold the per-listing advisory lock (used-listing:{listingId}) where they have one;
    /// this does not take it, because the online checkout already holds it when it calls in.
    /// </summary>
    public interface IListingReservationService
    {
        /// <summary>Release whatever checkout <paramref name="listing"/>.ReservedSaleId points at (dead status 'abandoned').</summary>
        Task<ReservationRelease> Release(ShopListingRow listing, CancellationToken ct = default);

        /// <summary>Release the checkout <paramref name="saleId"/> holding listing <paramref name="listingId"/>,
        /// marking a still-pending sale <paramref name="deadStatus"/> ('abandoned' or 'failed').</summary>
        Task<ReservationRelease> Release(Guid listingId, Guid itemId, Guid tenantId, Guid saleId,
            string deadStatus = "abandoned", CancellationToken ct = default);
    }

    public class ListingReservationService : IListingReservationService
    {
        private readonly IShopListingRepository _listings;
        private readonly IBikeShopRepository _shop;
        private readonly IPaymentProvider _payments;

        public ListingReservationService(IShopListingRepository listings, IBikeShopRepository shop, IPaymentProvider payments)
        {
            _listings = listings;
            _shop = shop;
            _payments = payments;
        }

        public Task<ReservationRelease> Release(ShopListingRow listing, CancellationToken ct = default) =>
            listing.ReservedSaleId is Guid saleId
                ? Release(listing.Id, listing.ItemId, listing.TenantId, saleId, "abandoned", ct)
                : Task.FromResult(ReservationRelease.Released);

        public async Task<ReservationRelease> Release(Guid listingId, Guid itemId, Guid tenantId, Guid saleId,
            string deadStatus = "abandoned", CancellationToken ct = default)
        {
            var sale = await _shop.GetSale(saleId, tenantId);
            if (sale is null)
            {
                await _listings.ClearReservation(listingId, tenantId, saleId);
                return ReservationRelease.Released;
            }
            if (sale.Status == "paid") return ReservationRelease.AlreadyPaid;

            if (sale.Status == "pending")
            {
                if (!string.IsNullOrEmpty(sale.StripePaymentIntentId))
                {
                    var outcome = await _payments.CancelPaymentIntentAsync(
                        sale.StripePaymentIntentId, sale.StripeConnectedAccountId, ct);
                    if (outcome == "succeeded") return ReservationRelease.AlreadyPaid;
                    if (outcome != "canceled") return ReservationRelease.Blocked;
                }
                if (await _listings.MarkSaleDead(saleId, tenantId, deadStatus) == 0)
                {
                    // It left 'pending' under us: the webhook settled it, or another release won.
                    var now = await _shop.GetSale(saleId, tenantId);
                    if (now?.Status == "paid") return ReservationRelease.AlreadyPaid;
                }
            }

            // A refunded sale was a real sale; its line keeps the unit as the record. Only a sale
            // that never collected money lets go of the unit.
            await _listings.DetachDeadSaleLine(saleId, itemId, tenantId);
            await _listings.ClearReservation(listingId, tenantId, saleId);
            return ReservationRelease.Released;
        }
    }
}
