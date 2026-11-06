using Services.Repositories.Data.BikeShopData;

namespace Services.Repositories.Interfaces
{
    /// <summary>
    /// Used &amp; ex-rental listings (Script0302): a serialized unit on the sales floor at its own
    /// asking price, its photos, and the product-level 'used' badge. Its own repository rather than
    /// more members on IBikeShopRepository, which the register, the finalizer and the import all
    /// depend on; the listing paths only ever read the catalog, they never reshape it.
    ///
    /// Every statement carries tenant_id on every per-tenant table it touches. Listing state
    /// (active / reserved / sold / unlisted) is derived by Services.BikeShop.ListingRules from
    /// what these reads return; the repository never decides it.
    ///
    /// A reservation is cleared in exactly one place, ClearReservation, and only
    /// IListingReservationService calls it, after it has released the checkout (PaymentIntent
    /// cancelled, sale dead, line detached). Create and Unlist refuse a row that still carries one.
    /// </summary>
    public interface IShopListingRepository
    {
        // ── Reads ─────────────────────────────────────────────────────────────────
        /// <summary>Listings for the admin tab. <paramref name="status"/>: active | sold | unlisted | all.</summary>
        Task<List<ShopListingRow>> List(Guid tenantId, string status);
        Task<ShopListingRow?> Get(Guid id, Guid tenantId);
        /// <summary>The unit's open listing (unlisted_at IS NULL), whatever the unit's status; null when none.</summary>
        Task<ShopListingRow?> GetOpenForItem(Guid itemId, Guid tenantId);
        /// <summary>
        /// Open, published listings whose unit is on the floor and whose product and variant are
        /// still active. Reservation live-ness is NOT applied here (it is a rule, not SQL).
        /// </summary>
        Task<List<ShopListingRow>> ListPublic(Guid tenantId);
        /// <summary>Open listings (unit in 'for_sale') for these units, photos not attached. For the register.</summary>
        Task<List<ShopListingRow>> ListActiveForItems(IEnumerable<Guid> itemIds, Guid tenantId);
        /// <summary>The unit with its variant and product, for defaulting a title and checking its state.</summary>
        Task<ShopListingItemContext?> GetItemContext(Guid itemId, Guid tenantId);
        /// <summary>Products the storefront badges as used: sellable, published, active, condition 'used'.</summary>
        Task<List<Guid>> ListUsedProductIds(Guid tenantId);

        // ── Listing writes ────────────────────────────────────────────────────────
        /// <summary>
        /// One transaction: lock the unit; refuse unless it is available / maintenance / retired (or
        /// 'for_sale' with no open listing, a stranded unit being re-listed); refuse when a pending,
        /// paid or out rental that has not ended names the unit (RentalBooked, with its start); close
        /// a stranded open row, refusing if it still carries a reservation; insert the listing with
        /// the unit's status as prior_status; move the unit to 'for_sale'.
        /// </summary>
        Task<ListingWriteResult> Create(ShopItemListing listing);
        /// <summary>Price, grade, notes, title and published; never the unit or the reservation.</summary>
        Task<int> Update(ShopItemListing listing);
        /// <summary>
        /// One transaction: lock the listing and unit; refuse when the unit is sold or the row still
        /// carries a reservation (release it first); otherwise stamp unlisted_at and return a
        /// 'for_sale' unit to its prior_status. Already-unlisted is Ok.
        /// </summary>
        Task<ListingWriteResult> Unlist(Guid id, Guid tenantId);

        // ── Online reservation ────────────────────────────────────────────────────
        /// <summary>Point the open listing at the checkout's sale and stamp reserved_at = now().</summary>
        Task<bool> Reserve(Guid listingId, Guid tenantId, Guid saleId);
        /// <summary>Clear the reservation only if it still points at <paramref name="saleId"/>.
        /// Called by IListingReservationService only, after the checkout is released.</summary>
        Task<int> ClearReservation(Guid listingId, Guid tenantId, Guid saleId);
        /// <summary>
        /// Null the unit on a DEAD (failed / abandoned) sale's line so the unit can be sold again:
        /// uk_shop_sale_line_item is unique over every line that names a unit, paid or not. Touches
        /// nothing on a pending, paid or refunded sale.
        /// </summary>
        Task<int> DetachDeadSaleLine(Guid saleId, Guid itemId, Guid tenantId);

        // ── Sale status, tenant-scoped (IBikeShopRepository's MarkSaleFailed / SetSalePaymentIntent are by id only) ──
        /// <summary>pending -> failed | abandoned, at this tenant only. Rows affected (0 when the sale moved on).</summary>
        Task<int> MarkSaleDead(Guid saleId, Guid tenantId, string status);
        Task<int> SetSalePaymentIntent(Guid saleId, Guid tenantId, string paymentIntentId);

        // ── Photos ────────────────────────────────────────────────────────────────
        Task<List<ShopListingPhoto>> ListPhotos(Guid listingId, Guid tenantId);
        Task<int> CountPhotos(Guid listingId, Guid tenantId);
        /// <summary>Inserts at the end of the gallery; null when the listing is not this tenant's.</summary>
        Task<ShopListingPhoto?> AddPhoto(ShopListingPhoto photo);
        /// <summary>Deletes the row and returns its url (for the blob), or null when not found.</summary>
        Task<string?> DeletePhoto(Guid photoId, Guid listingId, Guid tenantId);
        /// <summary>Positions in the order given; ids not on this listing are ignored.</summary>
        Task<int> ReorderPhotos(Guid listingId, Guid tenantId, IReadOnlyList<Guid> photoIds);

        // ── Product condition ─────────────────────────────────────────────────────
        Task<int> SetProductCondition(Guid productId, Guid tenantId, string condition);
    }
}
