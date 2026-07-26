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
        // MTB sub-classification driving provisioning defaults + discovery display:
        // 'bike_park' | 'shuttle' | 'resort'. NULL for MX and legacy tenants.
        public string? VenueCategory { get; set; }
        public string Timezone { get; set; } = "UTC";
        public bool RequireReservationForPasses { get; set; }
        public bool RequireEmergencyContact { get; set; }
        public bool AllowEventSubscriptions { get; set; }
        // When true, gate staff must attest they verified the rider's photo ID against
        // the purchaser name before redeeming. Enforced server-side in RedemptionController.
        // A per-scan attestation only: nothing is recorded about the rider. Contrast with
        // RequireIdForWristband below, which is a STORED verification.
        public bool RequireIdAtCheckin { get; set; }
        // When true, a wristband cannot be issued until the rider has both signed the waiver
        // and had their ID/age verified and recorded. Unlike RequireIdAtCheckin the result
        // persists, so a rider is carded once and every later scan shows the tick. Enforced
        // server-side in WristbandController.Link.
        public bool RequireIdForWristband { get; set; }
        public string? StripeConnectAccountId { get; set; }
        public string? StripeConnectStatus { get; set; }      // pending | active | restricted
        // 'platform' (default): charges run on the platform account, internal split, monthly payout.
        // 'direct': charges run on the tenant's own connected account with an application fee = our
        // service fee; the tenant is merchant of record and there is no platform payout. Required for
        // tenants exceeding the $1M/yr card-network sub-merchant threshold. Super-admin controlled.
        public string StripeChargeMode { get; set; } = "platform";
        // Lazily provisioned the first time a cashier opens the mobile app at
        // this tenant — required for Stripe Terminal tap-to-pay.
        public string? StripeTerminalLocationId { get; set; }
        // Terminal Location on the tenant's OWN connected account, provisioned lazily for 'direct'
        // mode card-present sales. Separate from StripeTerminalLocationId (the platform-account one).
        public string? StripeConnectedTerminalLocationId { get; set; }
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

        /// <summary>
        /// Share of the tenant service charge the RENTER pays on a bike-shop rental (bps).
        /// 10000 = renter pays all (the default everywhere else in the system), 0 = the track
        /// absorbs it. The RATE is ServiceChargeBps — the same percentage events use; this only
        /// decides who funds it. Set on Rentals -> Settings.
        /// </summary>
        public int RentalRiderPaidServiceChargeBps { get; set; } = 10000;

        /// <summary>
        /// Sales tax on rentals, in basis points. NULL means never configured, which the UI warns
        /// about; 0 means deliberately untaxed. The refundable deposit is never taxed.
        /// </summary>
        public int? RentalTaxBps { get; set; }

        /// <summary>Is the renter-paid service fee part of the rental taxable base.</summary>
        public bool RentalTaxServiceChargeTaxable { get; set; } = true;

        /// <summary>
        /// Share of the service charge the CUSTOMER funds on a bike shop sale (bps). 0 = the track
        /// absorbs it out of their own margin and the customer sees no fee line, which is the
        /// default; 10000 = added to what they pay. Either way the charge is owed and booked. The
        /// RATE is ServiceChargeBps, the same one events use; this only decides who funds it.
        /// </summary>
        public int ShopBuyerPaidServiceChargeBps { get; set; }

        /// <summary>Tenant-wide rental damage-waiver ("insurance"): an optional add-on at rental
        /// checkout. When bought, the renter pays RentalInsuranceBps of the rental value and the
        /// refundable deposit hold is waived. Off by default.</summary>
        public bool RentalInsuranceEnabled { get; set; }
        public string? RentalInsuranceLabel { get; set; }
        /// <summary>Percent of the rented gear value, in basis points (1500 = 15%).</summary>
        public int RentalInsuranceBps { get; set; }
        public int? MonthlyServiceChargeCapCents { get; set; }
        public string? ShippingName { get; set; }
        public string? AboutHtml { get; set; }
        public string? HoursJson { get; set; }
        public string? HomeNextUpTitle { get; set; }
        public Guid[]? HomeNextUpEventTypeIds { get; set; }
        public string? HomeBenefitsHtml { get; set; }
        public string? HomeSectionsJson { get; set; }   // { sectionKey: bool }; missing key = visible
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
        // First time this tenant was ever published (stamped by a trigger, never reset).
        // NULL = never published. Used by the stage->prod promotion import to refuse
        // overwriting a tenant that may hold real data.
        public DateTime? FirstPublishedAt { get; set; }
        public bool GiftCardsEnabled { get; set; } = false;
        public int GiftCardMinCents { get; set; } = 1000;       // $10 default
        public int GiftCardMaxCents { get; set; } = 50000;      // $500 default
        public bool RentalsEnabled { get; set; } = false;
        public bool ExtrasEnabled { get; set; } = true;
        public bool SeasonPassesEnabled { get; set; } = true;
        /// <summary>Gate admission mode for season passes: 1 (SeasonPassAdmissionType.EventSignUp)
        /// requires a prior reservation for a specific event before the gate admits the holder;
        /// 2 (SeasonPassAdmissionType.WalkUp, the default) admits on scan alone, with or without a
        /// calendar event running that day. Stored as an int to match the plain-column mapping
        /// convention used throughout this class; cast to SeasonPassAdmissionType at the call site.</summary>
        public int SeasonPassAdmissionTypeId { get; set; } = (int)SeasonPassAdmissionType.WalkUp;
        public bool ConcessionsEnabled { get; set; } = false;

        /// <summary>
        /// The standing "any active season pass holder gets X off" perk. Does NOT govern per-pass
        /// benefits (season_pass_benefit), which are product configuration.
        /// </summary>
        public bool SeasonPassDiscountEnabled { get; set; } = false;
        /// <summary>'percent' (basis points) or 'amount' (cents).</summary>
        public string SeasonPassDiscountKind { get; set; } = "percent";
        public int SeasonPassDiscountValue { get; set; }

        // Where the perk applies. The AMOUNT is shared (a track has one idea of "the pass holder
        // discount") but the surfaces are a deliberate choice, because a percentage picked with a
        // $9 burger in mind is the same percentage off a $6,000 bike.
        public bool SeasonPassDiscountAppliesConcession { get; set; } = true;
        public bool SeasonPassDiscountAppliesRetail { get; set; } = true;
        public bool SeasonPassDiscountAppliesRental { get; set; } = true;

        /// <summary>Does the tenant-wide pass discount apply to this benefit surface?</summary>
        public bool SeasonPassDiscountAppliesTo(string benefitType) => SeasonPassDiscountEnabled && benefitType switch
        {
            "concession" => SeasonPassDiscountAppliesConcession,
            "retail" => SeasonPassDiscountAppliesRetail,
            "rental" => SeasonPassDiscountAppliesRental,
            // Events are admission, not a retail perk: a standing "% off for pass holders" must
            // never quietly become a discount on race entry. Per-pass event benefits handle that.
            _ => false,
        };
        public bool BikeShopEnabled { get; set; } = false;
        /// <summary>Days after pickup to email a service reminder. 0 = off, the default: a track
        /// opts in to contacting customers months later rather than discovering it did.</summary>
        public int ShopServiceReminderDays { get; set; }
        /// <summary>Email the customer when a repair is marked ready. Defaults ON: transactional,
        /// free, and the customer is waiting on it.</summary>
        public bool ShopReadyNotifyEmail { get; set; } = true;
        /// <summary>Text the customer when a repair is marked ready. Defaults OFF: every send
        /// bills the tenant, so it can't switch itself on just because Twilio is configured.</summary>
        public bool ShopReadyNotifySms { get; set; }
        /// <summary>Shop supply fee as basis points of the LABOR subtotal on a repair bill
        /// (500 = 5%). 0 = off. Labor only: a percentage of an expensive part would track the
        /// part's price rather than the consumables the job actually burned.</summary>
        public int ShopSupplyFeeBps { get; set; }
        /// <summary>Ceiling on that fee in cents; null = uncapped.</summary>
        public int? ShopSupplyFeeCapCents { get; set; }
        public string ShopSupplyFeeLabel { get; set; } = "Shop supplies";
        /// <summary>Default shop labor rate in cents per hour. Null = no rate set, so labor lines
        /// take a typed price. When set, a labor line entered by hours bills hours * this rate.</summary>
        public int? ShopLaborRateCents { get; set; }
        public bool WristbandsEnabled { get; set; } = false;

        // ── Staff access policy (Script0239) ──────────────────────────────────
        /// <summary>0 = off (default), 1 = enforce. Gates whether the location and hours rules
        /// below are applied to the money-moving permissions at all.</summary>
        public int StaffAccessPolicyMode { get; set; }
        /// <summary>Networks the track operates from, in CIDR form. Empty = no location rule.</summary>
        public string[] StaffAllowedCidrs { get; set; } = Array.Empty<string>();
        /// <summary>Tenant-LOCAL window operations are allowed in. Null (both) = no clock rule.
        /// An end at or before the start means the window crosses midnight.</summary>
        public TimeSpan? StaffHoursStart { get; set; }
        public TimeSpan? StaffHoursEnd { get; set; }

        // ── Staff alert tripwires (Script0240) ────────────────────────────────
        /// <summary>Email the tenant's contact address when the previous day's staff activity
        /// trips a rule. Off by default: alerts that fire on normal behavior get ignored.</summary>
        public bool StaffAlertsEnabled { get; set; }
        /// <summary>One staff member's total refunds in a single local day, above which the
        /// digest flags them. Default 50000 ($500).</summary>
        public int StaffAlertRefundCents { get; set; } = 50000;

        /// <summary>Whether more than one discount may combine on a single sale (Script0254).
        /// False (the default) means the largest single discount applies and the rest are ignored,
        /// so a pass benefit, a staff discount and a promo code can't compound.</summary>
        public bool AllowDiscountStacking { get; set; }
        /// <summary>Trackside handout export on the rider reports. A motocross-race
        /// artifact: defaults on for motocross tenants, off for mountain-bike.</summary>
        public bool TracksideExportEnabled { get; set; } = true;
        public bool BlogEnabled { get; set; } = false;
        // Stepped event-ticket price ladders (price rises by date or sales volume).
        // Gates CONFIGURING steps; already-configured ladders keep resolving.
        public bool DynamicPricingEnabled { get; set; } = false;
        // Race-entry tiers minting single-use share coupons at purchase. Gates both
        // configuring bundles and minting new codes at purchase time.
        public bool BundledCouponsEnabled { get; set; } = false;
        // When set, this tenant is a LoamPassMx track mapped to this LoamMx destination id.
        // NULL = not a LoamPassMx track. Super-admin controlled.
        public string? LoampassMxDestinationId { get; set; }
        // Deployment model (super-admin controlled): 'hosted' (default subdomain),
        // 'custom_domain', or 'embedded'. CustomDomain / Embed* hold the concrete config.
        public string ClientType { get; set; } = "hosted";
        public string? CustomDomain { get; set; }
        // Set true once the custom domain actually serves (DNS+TLS+resolution); gates
        // the subdomain->custom-domain redirect so we never forward to a dead domain.
        public bool CustomDomainVerified { get; set; }
        public bool EmbedEnabled { get; set; }
        // Origins (scheme + host) allowed to frame the embed widgets (CSP frame-ancestors).
        public string[]? EmbedAllowedOrigins { get; set; }
        // An embedded client's own website pages. Drive the subdomain redirect (home)
        // and the apex link targeting (events page, falling back to home).
        public string? ExternalHomeUrl { get; set; }
        public string? ExternalEventsUrl { get; set; }
        // Where an apex event click lands for an embedded client: 'external' (their
        // site) or 'ridepass' (the hosted {subdomain}.ridepass.io/Event/:id page).
        public string EmbedEventTarget { get; set; } = "external";
        public bool AllowSelfCancel { get; set; } = false;
        // Rider-facing headings for the gate-fee sections at checkout / event pricing.
        // NULL = platform defaults ("Riding Pass" / "Spectator Pass").
        public string? RiderGateLabel { get; set; }
        public string? SpectatorGateLabel { get; set; }
        // Super-admin-gated platform features, both OFF by default (Script0180). Most tracks don't
        // want a waitlist, and the ones that do usually want the notify-only version.
        public bool WaitlistEnabled { get; set; } = false;
        // Charge the rider at JOIN time and auto-confirm them when a spot opens, instead of texting
        // a pay-now link. Holds a rider's money against a spot that may never open, so it's a
        // separate deliberate decision. Inert unless WaitlistEnabled is also true.
        public bool WaitlistPrepayEnabled { get; set; } = false;
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
        // Set when the stage/local demo seeder has populated this tenant (hides the button). NULL = never.
        public DateTime? SeedDataPopulatedAt { get; set; }
    }
}
