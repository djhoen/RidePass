namespace Services.Repositories.Data.TenantData
{
    public class Tenant
    {
        public Guid Id { get; set; }
        public string Subdomain { get; set; } = null!;
        public string DisplayName { get; set; } = null!;
        public string Status { get; set; } = null!;
        // 'motocross' | 'mountain_bike'. Drives provisioning defaults at creation
        // time (event types, waiver wording). Changed at the database via
        // migration only — there's no admin UI to flip an existing tenant's type
        // because doing so wouldn't retroactively rewrite their event types.
        public string TenantType { get; set; } = "motocross";
        public string Timezone { get; set; } = "UTC";
        public bool RequireReservationForPasses { get; set; }
        public bool RequireEmergencyContact { get; set; }
        public bool AllowEventSubscriptions { get; set; }
        public string? StripeConnectAccountId { get; set; }
        public string? StripeConnectStatus { get; set; }      // pending | active | restricted
        // Lazily provisioned the first time a cashier opens the mobile app at
        // this tenant — required for Stripe Terminal tap-to-pay.
        public string? StripeTerminalLocationId { get; set; }
        // Per-tenant Twilio Subaccount, provisioned lazily via Settings → SMS.
        // AuthToken is stored encrypted via EncryptionHelper; consumers must
        // decrypt before passing to Twilio. Until populated, SMS sends fall
        // back to the global Sms:Twilio:* config (transition-only fallback).
        public string? TwilioSubaccountSid { get; set; }
        public string? TwilioAuthTokenEncrypted { get; set; }
        public string? TwilioFromNumber { get; set; }       // E.164, e.g. +18885551234
        // Per-tenant Messaging Service that owns the sender pool (currently
        // a single toll-free number; short codes / 10DLC long codes attach
        // here later). When set, TwilioSmsSender routes through this instead
        // of binding directly to TwilioFromNumber.
        public string? TwilioMessagingServiceSid { get; set; }
        public bool SmsEnabled { get; set; }
        public DateTime? SmsEnabledAtUtc { get; set; }
        public int ServiceChargeBps { get; set; }
        public int? MonthlyServiceChargeCapCents { get; set; }
        public string? ShippingName { get; set; }
        public string? AboutHtml { get; set; }
        public string? HoursJson { get; set; }
        public string? HomeNextUpTitle { get; set; }
        public Guid[]? HomeNextUpEventTypeIds { get; set; }
        public bool? DailyStatusOpen { get; set; }
        public string? DailyStatusMessage { get; set; }
        public DateTime? DailyStatusUpdatedAt { get; set; }
        public string? ContactEmail { get; set; }
        public string? Phone { get; set; }
        public string? SocialFacebookUrl { get; set; }
        public string? SocialInstagramUrl { get; set; }
        public string? SocialTiktokUrl { get; set; }
        public string? SocialYoutubeUrl { get; set; }
        public string? RefundPolicyHtml { get; set; }
        public string? AddressLine { get; set; }
        public string? City { get; set; }
        public string? Region { get; set; }
        public string? PostalCode { get; set; }
        public string? Country { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        // Gates appearance in public discovery (apex map / featured / Discover /
        // events). Does NOT gate subdomain resolution. New tenants start false.
        public bool IsPublished { get; set; }
        public bool GiftCardsEnabled { get; set; } = false;
        public int GiftCardMinCents { get; set; } = 1000;       // $10 default
        public int GiftCardMaxCents { get; set; } = 50000;      // $500 default
        public bool RentalsEnabled { get; set; } = false;
        public bool ExtrasEnabled { get; set; } = false;
        public bool SeasonPassesEnabled { get; set; } = true;
        public bool AllowSelfCancel { get; set; } = false;
        public bool WaitlistEnabled { get; set; } = true;
        public int WaitlistConfirmWindowMinutes { get; set; } = 20;
        public bool MembershipEnabled { get; set; } = false;
        public string MembershipName { get; set; } = "Track Membership";
        public int MembershipPriceCents { get; set; } = 0;
        public string MembershipDurationKind { get; set; } = "yearly";   // 'one_time' | 'yearly'
        // Audience-shaped membership gates. Riders = race entries + day passes +
        // season passes. Spectators = extras (Gate Fees, camping, etc.).
        public bool MembershipRequiredForRiders { get; set; } = true;
        public bool MembershipRequiredForSpectators { get; set; } = false;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
