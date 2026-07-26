using Microsoft.Extensions.Logging;
using Services.Repositories.Data.BikeShopData;

namespace Services.Distributors
{
    /// <summary>
    /// Quality Bicycle Products, the largest North American bike parts distributor.
    ///
    /// WHAT IS DONE HERE, AND WHAT IS NOT.
    ///
    /// Everything around the wire call is complete and exercised: credential storage, scheduling,
    /// provenance stamping, mapping into the catalog, and the licensing guard that keeps CLS
    /// content out of the shared parts library. The one piece deliberately left unimplemented is
    /// <see cref="FetchRawRows"/>, the actual transport, because QBP's Content Licensing Service
    /// endpoints and payload schema are not public: they are behind a dealer login, and every
    /// integrator (Lightspeed, Masterlinq, Finale) is handed them under a dealer account. Guessing
    /// a URL and a JSON shape would produce something that looks finished and cannot work.
    ///
    /// TO FINISH IT you need a QBP dealer account with Content Licensing enabled, and their CLS
    /// documentation. Then implement FetchRawRows to return one <see cref="QbpRow"/> per item, and
    /// set <see cref="IsConfigured"/> to true. Nothing else in the pipeline needs to change: the
    /// mapping below, the sweep, the settings screen and the provenance rules all already work.
    ///
    /// Note QBP exposes TWO feeds and this is the second one:
    ///   API1  dealer-specific, invoice and inventory level, free to dealers.
    ///   API3 / CLS  full product content including names, UPCs and MPNs. Per-dealer licence, and
    ///               the reason ManufacturerNameSource is 'qbp' rather than something poolable.
    /// </summary>
    public class QbpCatalogSource : IDistributorCatalogSource
    {
        private readonly ILogger<QbpCatalogSource> _logger;

        public QbpCatalogSource(ILogger<QbpCatalogSource> logger)
        {
            _logger = logger;
        }

        public string Slug => "qbp";
        public string DisplayName => "Quality Bicycle Products";

        // NOT poolable, and that is the whole point of this property. CLS content is licensed to
        // the DEALER; pooling it into a library other dealers read would be redistribution.
        public string ManufacturerNameSource => Services.BikeShop.ManufacturerNameSource.Qbp;

        /// <summary>
        /// False until FetchRawRows is implemented against QBP's real CLS API. The sweep checks
        /// this and skips, so a shop never sees a nightly "sync failed" they cannot act on.
        /// </summary>
        public bool IsConfigured => false;

        private const string NotWiredMessage =
            "QBP syncing isn't switched on for this deployment yet. It needs QBP's Content Licensing "
            + "documentation, which they issue with a dealer account. Until then you can keep the "
            + "catalog current by exporting it from qbp.com and uploading the CSV under Import.";

        public Task<(bool Ok, string? Error)> TestConnection(DistributorCredentials credentials,
            CancellationToken ct = default)
        {
            if (!IsConfigured) return Task.FromResult((false, (string?)NotWiredMessage));
            throw new NotImplementedException();
        }

        public async Task<DistributorCatalog> FetchCatalog(DistributorCredentials credentials,
            CancellationToken ct = default)
        {
            if (!IsConfigured) throw new InvalidOperationException(NotWiredMessage);

            var rows = await FetchRawRows(credentials, ct);
            return MapToCatalog(rows);
        }

        /// <summary>
        /// THE ONE PIECE TO IMPLEMENT. One row per sellable item from QBP's CLS feed.
        /// Everything downstream of this is written and tested.
        /// </summary>
        protected virtual Task<List<QbpRow>> FetchRawRows(DistributorCredentials credentials, CancellationToken ct)
            => throw new NotImplementedException(NotWiredMessage);

        /// <summary>
        /// QBP rows to the shape ImportCatalog already understands. Kept separate from the
        /// transport, and public rather than private, so it can be tested against a handful of
        /// hand-written rows without any network access at all. That matters more than usual here:
        /// the transport can't be tested until someone has a dealer account, so the mapping is the
        /// only part of this class that CAN be verified today, and it holds the decisions that
        /// matter (what a sync may overwrite, and that its names stay out of the shared library).
        /// </summary>
        public DistributorCatalog MapToCatalog(List<QbpRow> rows)
        {
            // Which columns this feed CARRIES. ImportCatalog updates only these, which is what makes
            // a nightly refresh safe: no price column here means a shop's own retail prices are
            // never touched, and stock is never touched by an import at all.
            //
            // Note what is absent: Price. Cost and MSRP come from the distributor; what a shop
            // CHARGES is theirs, and a nightly job must never overwrite it.
            var present = new HashSet<string>
            {
                ShopImportColumn.Sku,
                ShopImportColumn.Barcode,
                ShopImportColumn.Mpn,
                ShopImportColumn.ManufacturerName,
                ShopImportColumn.VendorPartNumber,
                ShopImportColumn.Brand,
                ShopImportColumn.Cost,
                ShopImportColumn.Msrp,
            };

            var products = new List<ShopImportProduct>();
            // Group by the manufacturer's product name so a jersey's sizes land as variants of one
            // product rather than a product each, matching how the CSV import groups by Product.
            foreach (var group in rows.Where(r => !string.IsNullOrWhiteSpace(r.ProductName))
                                      .GroupBy(r => r.ProductName!.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                var first = group.First();
                products.Add(new ShopImportProduct
                {
                    // Seeds the shop's own product name on FIRST import only; ImportCatalog matches
                    // existing rows by GTIN/MPN/SKU and never renames a product a shop has since
                    // renamed. Their naming stays theirs.
                    Name = group.Key,
                    Brand = string.IsNullOrWhiteSpace(first.Brand) ? null : first.Brand.Trim(),
                    CategoryName = string.IsNullOrWhiteSpace(first.Category) ? null : first.Category.Trim(),
                    SupplierName = DisplayName,
                    Variants = group.Select(r => new ShopImportVariant
                    {
                        Sku = Trim(r.DealerSku),
                        Barcode = Trim(r.Upc),
                        Mpn = Trim(r.Mpn),
                        // The manufacturer's own wording, stamped 'qbp' by the sync so the shared
                        // library never receives it.
                        ManufacturerName = group.Key,
                        VendorPartNumber = Trim(r.QbpItemNumber),
                        Size = Trim(r.Size),
                        Color = Trim(r.Color),
                        CostCents = ToCents(r.DealerPrice),
                        MsrpCents = ToCents(r.Msrp),
                        TrackingKind = "pool",
                    }).ToList(),
                });
            }

            _logger.LogInformation("QBP catalog mapped: {Products} products, {Variants} variants",
                products.Count, products.Sum(p => p.Variants.Count));
            return new DistributorCatalog(products, present);
        }

        private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        private static int? ToCents(decimal? amount) =>
            amount is null or < 0 ? null : (int)Math.Round(amount.Value * 100m, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// One item as QBP's CLS feed describes it. Field names follow the concepts QBP's own
    /// documentation uses rather than our schema, so the mapping stays the only place the two
    /// vocabularies meet.
    /// </summary>
    public class QbpRow
    {
        /// <summary>The manufacturer's product name. Becomes ManufacturerName on the variant and
        /// seeds the shop's product name on first import.</summary>
        public string? ProductName { get; set; }
        public string? Brand { get; set; }
        public string? Category { get; set; }
        /// <summary>QBP's own item number, which is what a purchase order references.</summary>
        public string? QbpItemNumber { get; set; }
        /// <summary>Manufacturer part number.</summary>
        public string? Mpn { get; set; }
        public string? Upc { get; set; }
        public string? DealerSku { get; set; }
        public string? Size { get; set; }
        public string? Color { get; set; }
        /// <summary>What this dealer pays. Becomes cost, never the shop's retail price.</summary>
        public decimal? DealerPrice { get; set; }
        public decimal? Msrp { get; set; }
    }
}
