namespace Services.BikeShop
{
    /// <summary>Identity a lookup vendor returned for a barcode. No pricing, deliberately: even if
    /// a vendor returns one it must not reach platform_part.</summary>
    public record PartLookupResult(string Gtin14, string Name, string? Brand, string? Mpn, string? CategoryHint);

    /// <summary>
    /// Layer 4 of the scan resolver: ask an external GTIN database about a barcode nobody on the
    /// platform has ever seen. Behind an interface because whether this is switched on at all is a
    /// licensing decision, not a technical one.
    ///
    /// WHY NO VENDOR IS IMPLEMENTED YET. Two vendors were evaluated:
    ///   * Go-UPC ($74.95/mo, 5,000 requests/month) explicitly bars redistributing product data and
    ///     requires deleting it on termination. Caching their answers into a library that other
    ///     shops then read is exactly what that forbids, absent a written exception.
    ///   * UPCitemdb ($99/mo, 20,000 lookups/day) is silent on caching and licenses the service
    ///     "solely for Customer's operations", which is arguable either way and should be arguable
    ///     in writing before money and other people's shops depend on it.
    ///
    /// So the seam exists, the resolver calls it, and the only implementation is the disabled one.
    /// Adding a real vendor is: implement this, register it in Program.cs, and store rows with
    /// Source set to the vendor's slug so IPlatformPartRepository.PurgeSource can honour a
    /// termination clause in one statement.
    /// </summary>
    public interface IPartLookupProvider
    {
        /// <summary>Vendor slug written to platform_part.source, so a licensing purge can find
        /// exactly the rows that came from this vendor.</summary>
        string SourceSlug { get; }

        /// <summary>False when no vendor is configured, which is the default. The resolver skips
        /// the call entirely rather than paying a round trip to learn nothing.</summary>
        bool IsEnabled { get; }

        Task<PartLookupResult?> Lookup(string gtin14, CancellationToken ct = default);
    }

    /// <summary>
    /// The default: no external vendor. Every lookup misses, which is not a degraded mode. The
    /// library still fills from shops confirming parts through use at the counter, and that source
    /// carries no third-party terms at all.
    /// </summary>
    public class DisabledPartLookupProvider : IPartLookupProvider
    {
        public string SourceSlug => "disabled";
        public bool IsEnabled => false;
        public Task<PartLookupResult?> Lookup(string gtin14, CancellationToken ct = default)
            => Task.FromResult<PartLookupResult?>(null);
    }
}
