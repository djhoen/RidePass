using Services.Repositories.Data.BikeShopData;

namespace Services.Repositories.Interfaces
{
    /// <summary>
    /// The shared parts library. This is the ONE repository in the bike shop that is deliberately
    /// not tenant-scoped, which is why every method that touches tenant data takes the tenant id
    /// explicitly rather than the table carrying one: reads return identity that is safe for any
    /// shop to see, and the only per-tenant state (who confirmed what) lives in a separate table
    /// that is never projected into a response.
    ///
    /// See Script0248_PlatformPartLibrary.sql for why this exception is safe and what would make
    /// it unsafe.
    /// </summary>
    public interface IPlatformPartRepository
    {
        /// <summary>Identity for a normalised GTIN-14, or null if the library has never seen it.</summary>
        Task<PlatformPart?> GetByGtin(string gtin14);

        /// <summary>
        /// Records that <paramref name="tenantId"/>'s catalog says this GTIN is this part, creating
        /// the library entry if it is new. Idempotent per tenant: a shop scanning the same part all
        /// day confirms it once, so TimesConfirmed keeps meaning "independent shops agree".
        /// Returns the library entry's id so the caller can link its variant to it.
        /// </summary>
        Task<Guid> Confirm(Guid tenantId, string gtin14, string name, string? brand, string? mpn, string? categoryHint);

        /// <summary>
        /// Stores a vendor's answer for a barcode nobody on the platform has seen, tagged with the
        /// vendor's slug so <see cref="PurgeSource"/> can honour a termination clause. Deliberately
        /// separate from <see cref="Confirm"/>: a vendor is not a shop confirming anything, so this
        /// never touches TimesConfirmed and never claims 'tenant_confirmed'. Returns the stored
        /// entry, or the existing one if another request cached it first.
        /// </summary>
        Task<PlatformPart> CacheFromVendor(string sourceSlug, Services.BikeShop.PartLookupResult result);

        /// <summary>
        /// Deletes every row contributed by one external vendor. This is the licensing kill switch:
        /// Go-UPC's terms require deleting product data on termination, so honouring that has to be
        /// a single call. shop_variant.platform_part_id is ON DELETE SET NULL, so no shop's own
        /// catalog is damaged. Returns the number of rows removed.
        /// </summary>
        Task<int> PurgeSource(string source);

        /// <summary>Row counts by source, for the super-admin view of what the library is made of.</summary>
        Task<List<PlatformPartSourceCount>> CountsBySource();
    }

    public record PlatformPartSourceCount(string Source, int PartCount);
}
