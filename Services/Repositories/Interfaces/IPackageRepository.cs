using Services.Repositories.Data.PackageData;

namespace Services.Repositories.Interfaces
{
    public interface IPackageRepository
    {
        // ── Admin ────────────────────────────────────────────────────────────
        Task<List<PackageProduct>> ListByTenant(Guid tenantId);
        /// <summary>Full package with tiers, slots, and items hydrated.</summary>
        Task<PackageProduct?> GetById(Guid id, Guid tenantId);
        Task<Guid> Create(PackageProduct p);
        Task Update(PackageProduct p);
        Task Delete(Guid id, Guid tenantId);
        /// <summary>Replace the whole set of tiers / slots / items for a package (admin save).</summary>
        Task ReplaceTiers(Guid packageId, Guid tenantId, IEnumerable<PackageTier> tiers);
        Task ReplaceSlots(Guid packageId, Guid tenantId, IEnumerable<PackageSessionSlot> slots);
        Task ReplaceItems(Guid packageId, Guid tenantId, IEnumerable<PackageItem> items);

        // ── Public ───────────────────────────────────────────────────────────
        Task<List<PackageProduct>> ListPublic(Guid tenantId);
        Task<PackageProduct?> GetBySlugOrId(string slugOrId, Guid tenantId);

        // ── Booking ──────────────────────────────────────────────────────────
        /// <summary>How many non-cancelled package bookings already hold this slot on this date.</summary>
        Task<int> CountSlotBookings(Guid slotId, DateTime rideDate);
        Task<Guid> CreatePurchase(PackagePurchase purchase);
        Task<PackagePurchase?> GetPurchase(Guid id, Guid tenantId);
        Task<PackagePurchase?> GetPurchaseByPaymentIntent(string paymentIntentId);
        Task SetPurchasePaymentIntent(Guid id, string paymentIntentId, string? depositIntentId, string? connectedAccountId);
        Task SetPurchaseArtifacts(Guid id, Guid? ticketPurchaseId, Guid? shopRentalId);
        Task<bool> TryMarkPurchasePaid(Guid id, Guid tenantId, int orderNumber);
        Task MarkPurchaseFailed(Guid id);
    }
}
