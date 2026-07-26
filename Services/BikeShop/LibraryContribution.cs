namespace Services.BikeShop
{
    /// <summary>
    /// Where a variant's manufacturer_name came from. Stored on the row because the answer decides
    /// whether that name may be pooled into the cross-tenant parts library, and that decision
    /// cannot be re-derived later from the string itself.
    /// </summary>
    public static class ManufacturerNameSource
    {
        /// <summary>A human at the shop typed it. RidePass's own data.</summary>
        public const string Shop = "shop";
        /// <summary>Came in on a CSV the shop uploaded.</summary>
        public const string Import = "import";
        /// <summary>Came back out of the shared library, so pooling it is a no-op.</summary>
        public const string Library = "library";
        /// <summary>From QBP's Content Licensing Service. Licensed to that DEALER, not to us.</summary>
        public const string Qbp = "qbp";
        /// <summary>From the fake distributor used to exercise the sync on dev/staging. Kept OUT of
        /// the poolable list on purpose so a test run demonstrates the licensing guard instead of
        /// quietly bypassing it.</summary>
        public const string Sample = "sample";
    }

    /// <summary>
    /// The single rule deciding what a shop's catalog may contribute to the cross-tenant parts
    /// library. It lives here, as a pure function, so it can be tested and so there is exactly one
    /// place to change if the policy ever moves.
    ///
    /// TWO INDEPENDENT GUARDS, for two different failure modes:
    ///
    ///   1. TENANT PRIVACY. A product name a tenant typed belongs to that tenant and must never be
    ///      visible to another tenant, so only the MANUFACTURER's name is eligible at all. There is
    ///      deliberately no fallback to the shop's own product name when that is missing.
    ///
    ///   2. THIRD-PARTY LICENSING. Even a genuine manufacturer name may be un-poolable depending on
    ///      where it came from. QBP's Content Licensing Service is licensed per dealer, so pooling
    ///      one dealer's CLS content into a library other dealers read is redistribution. Without
    ///      this guard, switching on the distributor sync would have started doing exactly that
    ///      silently, with no code change anywhere.
    ///
    /// The allow-list is deliberately an ALLOW-list: an unrecognised or missing source contributes
    /// nothing. A new distributor is then un-poolable until someone reads its terms and adds it
    /// here, which is the correct default for a decision with legal consequences.
    /// </summary>
    public static class LibraryContribution
    {
        private static readonly HashSet<string> PoolableSources = new(StringComparer.OrdinalIgnoreCase)
        {
            ManufacturerNameSource.Shop,
            ManufacturerNameSource.Import,
            ManufacturerNameSource.Library,
        };

        /// <summary>Whether names from this source may be pooled. Null/unknown is always false.</summary>
        public static bool IsPoolableSource(string? source) =>
            source is not null && PoolableSources.Contains(source);

        /// <summary>
        /// The name to contribute for a scanned part, or null when this part must not be
        /// contributed at all.
        /// </summary>
        /// <param name="gtin14">Normalised GTIN-14, or null if what was scanned wasn't a barcode.
        /// A shop's own SKU is not shareable identity: it means nothing to another shop, and
        /// pooling on it would let one shop's private numbering collide with another's.</param>
        /// <param name="manufacturerName">The manufacturer's own name for the part.</param>
        /// <param name="manufacturerNameSource">One of <see cref="ManufacturerNameSource"/>.</param>
        public static string? NameToContribute(string? gtin14, string? manufacturerName,
            string? manufacturerNameSource)
        {
            if (string.IsNullOrWhiteSpace(gtin14)) return null;
            if (!IsPoolableSource(manufacturerNameSource)) return null;
            var name = manufacturerName?.Trim();
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
    }
}
