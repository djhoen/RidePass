using Services.Repositories.Data.BikeShopData;

namespace Services.Distributors
{
    /// <summary>Credentials handed to a source, already decrypted. Never logged.</summary>
    public record DistributorCredentials(string? AccountNumber, string? Username, string? Password, string? ApiKey);

    /// <summary>
    /// What one fetch produced. Rows are shaped as <see cref="ShopImportProduct"/> on purpose: the
    /// sync then hands them straight to BikeShopRepository.ImportCatalog, reusing the match-by
    /// GTIN then MPN then SKU logic, the update-only-columns-the-feed-carried rule, and the
    /// never-touch-stock guarantee that the CSV import already has tested.
    /// </summary>
    public record DistributorCatalog(List<ShopImportProduct> Products, HashSet<string> PresentColumns);

    /// <summary>
    /// A distributor RidePass can pull a catalog from. One implementation per distributor, resolved
    /// from the credential row's slug.
    /// </summary>
    public interface IDistributorCatalogSource
    {
        /// <summary>Matches tenant_distributor_credential.distributor.</summary>
        string Slug { get; }

        /// <summary>Shown in the settings screen.</summary>
        string DisplayName { get; }

        /// <summary>
        /// Which <see cref="Services.BikeShop.ManufacturerNameSource"/> value names written from
        /// this feed are stamped with. This is what keeps a per-dealer licensed feed out of the
        /// shared parts library, so it must be the distributor's own slug and never 'shop'.
        /// </summary>
        string ManufacturerNameSource { get; }

        /// <summary>
        /// False when this deployment has no way to talk to the distributor (no transport
        /// implemented, or platform-level config missing). The sweep skips rather than failing
        /// every tenant's sync with an error they cannot act on.
        /// </summary>
        bool IsConfigured { get; }

        /// <summary>
        /// Cheap credential check for the settings screen's "Test connection" button. Should not
        /// pull the whole catalog.
        /// </summary>
        Task<(bool Ok, string? Error)> TestConnection(DistributorCredentials credentials, CancellationToken ct = default);

        /// <summary>Pull the dealer's catalog. Throws on transport or auth failure; the sweep
        /// records the message against the credential so the shop can see it.</summary>
        Task<DistributorCatalog> FetchCatalog(DistributorCredentials credentials, CancellationToken ct = default);
    }
}
