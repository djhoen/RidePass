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
        Task UpdateRequireIdAtCheckin(Guid tenantId, bool require);
        Task SetStripeConnectAccount(Guid tenantId, string accountId, string status);
        Task UpdateStripeConnectStatus(string accountId, string status);
        Task SetStripeChargeMode(Guid tenantId, string chargeMode);
        Task<Tenant?> GetByStripeConnectAccountId(string accountId);
        /// <summary>
        /// Reverse-lookup a tenant by its provisioned Twilio Subaccount SID.
        /// Used by the StatusCallback webhook: Twilio identifies the owning
        /// account in the AccountSid form param, and we need the matching
        /// tenant row to decrypt its auth token for signature verification.
        /// </summary>
        Task<Tenant?> GetByTwilioSubaccountSid(string subaccountSid);
        Task ClearStripeConnect(Guid tenantId);
        /// <summary>
        /// Persist the lazily-provisioned Stripe Terminal Location id (used by the
        /// mobile cashier app's tap-to-pay flow). Called once per tenant the first
        /// time a cashier opens the app at that tenant.
        /// </summary>
        Task SetStripeTerminalLocationId(Guid tenantId, string locationId);
        Task SetStripeConnectedTerminalLocationId(Guid tenantId, string locationId);
        /// <summary>
        /// Persist the freshly-provisioned Twilio Subaccount credentials and
        /// flip sms_enabled on. authTokenEncrypted must be the
        /// EncryptionHelper-encrypted form; the raw token is never stored.
        /// messagingServiceSid is the per-tenant MG that owns the sender pool —
        /// nullable for backward compatibility with tenants provisioned before
        /// MG routing existed, but new provisioning should always supply it.
        /// </summary>
        Task SetTwilioCredentials(Guid tenantId, string subaccountSid, string authTokenEncrypted,
            string fromNumber, string? messagingServiceSid);
        /// <summary>
        /// Wipe all Twilio columns and flip sms_enabled off. Called from the
        /// provisioner's release flow after the Twilio-side close succeeds.
        /// Idempotent: clearing already-null columns is a no-op.
        /// </summary>
        Task ClearTwilioCredentials(Guid tenantId);
        /// <summary>
        /// Toggle SMS on/off without clearing the credentials. Used by the
        /// Settings → SMS page's pause control so the tenant keeps their
        /// provisioned number + 10DLC brand registration while disabled.
        /// </summary>
        Task SetSmsEnabled(Guid tenantId, bool enabled);
        Task UpdateServiceCharge(Guid tenantId, int serviceChargeBps, int? monthlyCapCents);
        // Super-admin core edit: name/status/timezone + address (with geo) + contact.
        // Scoped to only these columns so it never clobbers shipping_name, socials, etc.
        Task UpdateAdminDetails(Guid tenantId, string displayName, string status, string timezone, bool isPublished,
            string? addressLine, string? city, string? region, string? postalCode, string? country,
            double? latitude, double? longitude, string? contactEmail, string? phone, string? loampassMxDestinationId,
            string clientType, string? customDomain, bool customDomainVerified, bool embedEnabled,
            string[]? embedAllowedOrigins, string? externalHomeUrl, string? externalEventsUrl, string embedEventTarget);
        // Super-admin feature toggles (boolean flags only).
        Task UpdateFeatures(Guid tenantId, bool giftCardsEnabled, bool rentalsEnabled, bool extrasEnabled,
            bool seasonPassesEnabled, bool concessionsEnabled, bool blogEnabled, bool membershipEnabled,
            bool waitlistEnabled, bool allowSelfCancel, bool dynamicPricingEnabled, bool bundledCouponsEnabled);
        Task UpdateLocation(Guid tenantId, string? shippingName, string? addressLine, string? city, string? region,
            string? postalCode, string? country, double? latitude, double? longitude);
        Task UpdateHomeContent(Guid tenantId, string? aboutHtml, string? hoursJson,
            string? homeNextUpTitle, Guid[]? homeNextUpEventTypeIds,
            string? homeBenefitsHtml, string? homeSectionsJson);
        Task UpdateDailyStatus(Guid tenantId, bool? open, string? message);
        Task UpdateFooter(Guid tenantId, string? contactEmail, string? phone,
            string? facebook, string? instagram, string? tiktok, string? youtube, string? refundPolicyHtml);
        Task UpdateGiftCardSettings(Guid tenantId, bool enabled, int minCents, int maxCents);
        Task UpdateRentalsEnabled(Guid tenantId, bool enabled);
        Task UpdateExtrasEnabled(Guid tenantId, bool enabled);
        Task UpdateSeasonPassesEnabled(Guid tenantId, bool enabled);
        Task UpdateConcessionsEnabled(Guid tenantId, bool enabled);
        Task UpdateBlogEnabled(Guid tenantId, bool enabled);
        Task UpdateCancellationPolicy(Guid tenantId, bool allowSelfCancel, bool waitlistEnabled, int waitlistConfirmWindowMinutes);
        Task UpdateGateLabels(Guid tenantId, string? riderGateLabel, string? spectatorGateLabel);
        Task UpdateMembershipSettings(
            Guid tenantId, bool enabled, string name, int priceCents, string durationKind,
            bool requiredForRiders, bool requiredForSpectators);
        Task<List<Tenant>> ListAll();
    }
}
