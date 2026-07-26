using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Services.BikeShop;
using Services.Distributors;
using Services.Repositories.Data.BikeShopData;
using Services.Repositories.Interfaces;

namespace UnitTests
{
    // Covers the ORCHESTRATION: what the sync tells ImportCatalog to do, which tenant it writes to,
    // and how outcomes are recorded. The QBP transport can't be reached without a dealer account,
    // so a fake source stands in for it; everything on this side of that call is the real code.
    //
    // The test that matters most is ProvenanceComesFromTheSource: it is the difference between a
    // nightly sync that quietly redistributes a distributor's per-dealer licensed content and one
    // that doesn't, and nothing about the strings involved would reveal which you had.
    [TestFixture]
    public class DistributorSyncServiceTests
    {
        // ── Fakes ────────────────────────────────────────────────────────────────────────
        private class FakeSource : IDistributorCatalogSource
        {
            public string Slug { get; init; } = "fakeco";
            public string DisplayName => "Fake Distributor";
            public string ManufacturerNameSource { get; init; } = Services.BikeShop.ManufacturerNameSource.Qbp;
            public bool IsConfigured { get; init; } = true;
            public Exception? ThrowOnFetch { get; init; }
            public List<ShopImportProduct> Products { get; init; } = new();
            public HashSet<string> Present { get; init; } = new();

            public Task<(bool Ok, string? Error)> TestConnection(DistributorCredentials c, CancellationToken ct = default)
                => Task.FromResult((true, (string?)null));

            public Task<DistributorCatalog> FetchCatalog(DistributorCredentials c, CancellationToken ct = default)
                => ThrowOnFetch is not null
                    ? Task.FromException<DistributorCatalog>(ThrowOnFetch)
                    : Task.FromResult(new DistributorCatalog(Products, Present));
        }

        private class FakeCatalog : ICatalogImporter
        {
            public Guid? CalledWithTenantId;
            public ShopImportOptions? CalledWithOptions;
            public int CallCount;

            public Task<ShopImportResult> ImportCatalog(Guid tenantId, List<ShopImportProduct> products,
                Guid? byUserId, ShopImportOptions? options = null)
            {
                CallCount++;
                CalledWithTenantId = tenantId;
                CalledWithOptions = options;
                return Task.FromResult(new ShopImportResult { Products = products.Count, Variants = 2, VariantsUpdated = 3 });
            }
        }

        private class FakeCredentials : IDistributorCredentialRepository
        {
            public TenantDistributorCredential? Row;
            public string? MarkedStatus;
            public string? MarkedError;
            public bool MarkedRunning;

            public Task<TenantDistributorCredential?> Get(Guid tenantId, string distributor) =>
                Task.FromResult(Row is not null && Row.TenantId == tenantId ? Row : null);
            public Task<List<DistributorConnectionStatus>> ListStatuses(Guid tenantId) =>
                Task.FromResult(new List<DistributorConnectionStatus>());
            public Task Upsert(Guid t, string d, string? a, string? u, string? p, string? k, bool e) => Task.CompletedTask;
            public Task Delete(Guid tenantId, string distributor) => Task.CompletedTask;
            public Task<List<TenantDistributorCredential>> ListDueForSync(DateTime staleBefore, int limit = 50) =>
                Task.FromResult(Row is null ? new List<TenantDistributorCredential>() : new() { Row });
            public Task MarkRunning(Guid id) { MarkedRunning = true; return Task.CompletedTask; }
            public Task MarkResult(Guid id, string status, string? error, int seen, int updated)
            {
                MarkedStatus = status; MarkedError = error; return Task.CompletedTask;
            }
        }

        private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");

        private static TenantDistributorCredential Credential(string slug = "fakeco") => new()
        {
            Id = Guid.NewGuid(), TenantId = TenantA, Distributor = slug, IsEnabled = true,
        };

        private static (DistributorSyncService Sync, FakeCatalog Catalog, FakeCredentials Creds)
            Build(FakeSource source, TenantDistributorCredential? credential = null)
        {
            var creds = new FakeCredentials { Row = credential ?? Credential(source.Slug) };
            var catalog = new FakeCatalog();
            var sync = new DistributorSyncService(creds, catalog, new[] { (IDistributorCatalogSource)source },
                NullLogger<DistributorSyncService>.Instance);
            return (sync, catalog, creds);
        }

        private static List<ShopImportProduct> OneProduct() => new()
        {
            new ShopImportProduct
            {
                Name = "Fly Racing Kinetic Jersey",
                Variants = new() { new ShopImportVariant { Sku = "JRS-M", ManufacturerName = "Fly Racing Kinetic Jersey" } },
            },
        };

        // ── The licensing guard ──────────────────────────────────────────────────────────
        [Test]
        public async Task ProvenanceComesFromTheSource_NotFromTheCaller()
        {
            // A distributor's names must be stamped with ITS slug, so LibraryContribution refuses to
            // pool them. If this ever came out as 'shop' or 'import', every nightly sync would start
            // feeding per-dealer licensed content into a library other dealers read.
            var source = new FakeSource
            {
                ManufacturerNameSource = Services.BikeShop.ManufacturerNameSource.Qbp,
                Products = OneProduct(),
            };
            var (sync, catalog, _) = Build(source);

            await sync.SyncTenant(TenantA, source.Slug);

            Assert.That(catalog.CalledWithOptions!.ManufacturerNameSource,
                Is.EqualTo(Services.BikeShop.ManufacturerNameSource.Qbp));
            Assert.That(LibraryContribution.IsPoolableSource(catalog.CalledWithOptions.ManufacturerNameSource),
                Is.False, "A distributor feed must never be stamped with a poolable source.");
        }

        [Test]
        public async Task DefaultImportProvenanceIsNotSilentlyInherited()
        {
            // ShopImportOptions defaults ManufacturerNameSource to 'import' (poolable) for the CSV
            // path. The sync MUST override it. This catches the case where someone constructs the
            // options without setting it and the default quietly makes licensed content shareable.
            var source = new FakeSource { Products = OneProduct() };
            var (sync, catalog, _) = Build(source);

            await sync.SyncTenant(TenantA, source.Slug);

            Assert.That(catalog.CalledWithOptions!.ManufacturerNameSource,
                Is.Not.EqualTo(Services.BikeShop.ManufacturerNameSource.Import));
        }

        // ── What a sync is allowed to do ─────────────────────────────────────────────────
        [Test]
        public async Task SyncMatchesAndUpdates_RatherThanDuplicating()
        {
            // Without UpdateExisting the second night collides on every unique index, and the shop
            // wakes up to a failed sync or a doubled catalog.
            var source = new FakeSource { Products = OneProduct() };
            var (sync, catalog, _) = Build(source);

            await sync.SyncTenant(TenantA, source.Slug);

            Assert.That(catalog.CalledWithOptions!.UpdateExisting, Is.True);
        }

        [Test]
        public async Task OnlyTheColumnsTheFeedCarriesArePassedThrough()
        {
            // ImportCatalog writes only PresentColumns, so this is what stops a feed with no price
            // column from blanking a shop's retail prices.
            var source = new FakeSource
            {
                Products = OneProduct(),
                Present = new HashSet<string> { ShopImportColumn.Cost, ShopImportColumn.Mpn },
            };
            var (sync, catalog, _) = Build(source);

            await sync.SyncTenant(TenantA, source.Slug);

            Assert.That(catalog.CalledWithOptions!.PresentColumns,
                Is.EquivalentTo(new[] { ShopImportColumn.Cost, ShopImportColumn.Mpn }));
        }

        [Test]
        public async Task WritesToTheCredentialsOwnTenant()
        {
            // The sweep is tenant-spanning and runs with no ambient tenant context, so the tenant
            // id must come off the credential row itself. Getting this wrong writes one shop's
            // distributor catalog into another shop's inventory.
            var source = new FakeSource { Products = OneProduct() };
            var (sync, catalog, _) = Build(source);

            await sync.SyncDueTenantsAsync();

            Assert.That(catalog.CalledWithTenantId, Is.EqualTo(TenantA));
        }

        // ── Outcomes ─────────────────────────────────────────────────────────────────────
        [Test]
        public async Task UnconfiguredSourceIsSkipped_NotFailed()
        {
            // A shop can do nothing about a transport this deployment hasn't wired, so it must not
            // be counted (or reported) as their failure.
            var source = new FakeSource { IsConfigured = false, Products = OneProduct() };
            var (sync, catalog, _) = Build(source);

            var result = await sync.SyncTenant(TenantA, source.Slug);

            Assert.That(result.Ok, Is.False);
            Assert.That(result.Skipped, Is.True);
            Assert.That(catalog.CallCount, Is.Zero, "Nothing should be written when the source can't run.");
        }

        [Test]
        public async Task SweepDoesNotEvenQueryWhenNoSourceIsConfigured()
        {
            var source = new FakeSource { IsConfigured = false };
            var (sync, _, _) = Build(source);

            var summary = await sync.SyncDueTenantsAsync();

            Assert.That(summary.TenantsConsidered, Is.Zero);
        }

        [Test]
        public async Task FetchFailureIsRecordedAgainstTheCredential()
        {
            var source = new FakeSource { ThrowOnFetch = new InvalidOperationException("bad api key") };
            var (sync, catalog, creds) = Build(source);

            var result = await sync.SyncTenant(TenantA, source.Slug);

            Assert.That(result.Ok, Is.False);
            Assert.That(result.Skipped, Is.False, "A real failure is not a skip.");
            Assert.That(creds.MarkedStatus, Is.EqualTo("error"));
            Assert.That(creds.MarkedError, Does.Contain("bad api key"));
            Assert.That(catalog.CallCount, Is.Zero, "A failed fetch must not write a partial catalog.");
        }

        [Test]
        public async Task SuccessIsRecordedAndReported()
        {
            var source = new FakeSource { Products = OneProduct() };
            var (sync, _, creds) = Build(source);

            var result = await sync.SyncTenant(TenantA, source.Slug);

            Assert.That(result.Ok, Is.True);
            Assert.That(result.ProductsSeen, Is.EqualTo(1));
            Assert.That(result.VariantsCreated, Is.EqualTo(2));
            Assert.That(result.VariantsUpdated, Is.EqualTo(3));
            Assert.That(creds.MarkedRunning, Is.True, "A long pull should be visible as running while it runs.");
            Assert.That(creds.MarkedStatus, Is.EqualTo("ok"));
            Assert.That(creds.MarkedError, Is.Null);
        }

        [Test]
        public async Task SyncingADistributorTheTenantHasNotConnectedIsRefused()
        {
            var source = new FakeSource { Products = OneProduct() };
            var (sync, catalog, _) = Build(source);

            var result = await sync.SyncTenant(Guid.NewGuid(), source.Slug);

            Assert.That(result.Ok, Is.False);
            Assert.That(catalog.CallCount, Is.Zero);
        }
    }
}
