using Services.Repositories.Data.TenantData;

namespace Services.Repositories.Interfaces
{
    public interface ITenantRepository
    {
        Task<Tenant?> GetBySubdomain(string subdomain);
        Task<Tenant?> GetById(Guid id);
        Task<Guid> Create(Tenant tenant);
        Task UpdateTimezone(Guid tenantId, string timezone);
        Task UpdateRequireReservation(Guid tenantId, bool require);
        Task UpdateRequireEmergencyContact(Guid tenantId, bool require);
        Task UpdateAllowEventSubscriptions(Guid tenantId, bool allow);
        Task SetStripeConnectAccount(Guid tenantId, string accountId, string status);
        Task UpdateStripeConnectStatus(string accountId, string status);
        Task<Tenant?> GetByStripeConnectAccountId(string accountId);
        Task ClearStripeConnect(Guid tenantId);
        /// <summary>
        /// Persist the lazily-provisioned Stripe Terminal Location id (used by the
        /// mobile cashier app's tap-to-pay flow). Called once per tenant the first
        /// time a cashier opens the app at that tenant.
        /// </summary>
        Task SetStripeTerminalLocationId(Guid tenantId, string locationId);
        Task UpdateServiceCharge(Guid tenantId, int serviceChargeBps, int? monthlyCapCents);
        Task UpdateLocation(Guid tenantId, string? shippingName, string? addressLine, string? city, string? region,
            string? postalCode, string? country, double? latitude, double? longitude);
        Task UpdateHomeContent(Guid tenantId, string? aboutHtml, string? hoursJson,
            string? homeNextUpTitle, Guid[]? homeNextUpEventTypeIds);
        Task UpdateDailyStatus(Guid tenantId, bool? open, string? message);
        Task UpdateFooter(Guid tenantId, string? contactEmail, string? phone,
            string? facebook, string? instagram, string? tiktok, string? youtube, string? refundPolicyHtml);
        Task UpdateGiftCardSettings(Guid tenantId, bool enabled, int minCents, int maxCents);
        Task UpdateRentalsEnabled(Guid tenantId, bool enabled);
        Task UpdateExtrasEnabled(Guid tenantId, bool enabled);
        Task UpdateSeasonPassesEnabled(Guid tenantId, bool enabled);
        Task UpdateCancellationPolicy(Guid tenantId, bool allowSelfCancel, bool waitlistEnabled, int waitlistConfirmWindowMinutes);
        Task UpdateMembershipSettings(
            Guid tenantId, bool enabled, string name, int priceCents, string durationKind,
            bool requiredForRiders, bool requiredForSpectators);
        Task<List<Tenant>> ListAll();
    }
}
