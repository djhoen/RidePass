namespace Services.Repositories.Data.BikeShopData
{
    /// <summary>
    /// One entry in the shared parts library: what a barcode IS, pooled across every shop on the
    /// platform. Deliberately NOT tenant-scoped (see Script0248 for the full reasoning).
    ///
    /// The absence of price, cost, margin, stock and supplier is the whole safety argument for
    /// sharing this across tenants. Do not add them. Money lives on shop_variant, which is
    /// tenant-scoped; a price here would be shop A reading shop B's margins.
    /// </summary>
    public class PlatformPart
    {
        public Guid Id { get; set; }

        /// <summary>Always the 14-digit normalised form. See Services.BikeShop.Gtin.</summary>
        public string Gtin14 { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;
        public string? Brand { get; set; }
        public string? Mpn { get; set; }

        /// <summary>A loose hint like "Tires", never a shop_category id: categories are per-tenant
        /// and every shop names them differently. Only seeds the "add product" form.</summary>
        public string? CategoryHint { get; set; }

        /// <summary>
        /// 'tenant_confirmed' (a shop confirmed it through use), 'staff', or a vendor slug for a
        /// cached external lookup. The vendor case is what makes a licensing purge one statement,
        /// so a vendor-sourced row must name its vendor rather than a generic 'external'.
        /// </summary>
        public string Source { get; set; } = "tenant_confirmed";

        /// <summary>
        /// How many DISTINCT shops have independently confirmed this identity. The quality signal:
        /// one shop scanning the same tube all day counts once.
        ///
        /// DERIVED on read by counting platform_part_confirmation, not a stored column, so it can
        /// never drift from the confirmations it claims to summarise. See Script0248.
        /// </summary>
        public int TimesConfirmed { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
