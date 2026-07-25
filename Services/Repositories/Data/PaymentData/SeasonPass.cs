namespace Services.Repositories.Data.PaymentData
{
    public class SeasonPassProduct
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public int PriceCents { get; set; }
        public DateTime ValidFromDate { get; set; }
        public DateTime ValidToDate { get; set; }
        public string Kind { get; set; } = "unlimited";       // unlimited | days_of_week | credits
        public int[]? ValidDaysOfWeek { get; set; }           // 0=Sun..6=Sat
        public int? TotalCredits { get; set; }
        public bool RequiresWaiver { get; set; }
        public int RiderPaidServiceChargeBps { get; set; }
        public bool IsActive { get; set; }
        public int SortOrder { get; set; }

        // Landing page (Script0228): a per-product marketing page at /SeasonPasses/{slug}.
        // LandingHtml is raw Tiptap HTML, sanitized at render (RichTextView contract).
        public string? Slug { get; set; }
        public string? HeroImageUrl { get; set; }
        public string? LandingHtml { get; set; }
        public bool LandingPublished { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class SeasonPassEventTypePerk
    {
        public Guid Id { get; set; }
        public Guid PassProductId { get; set; }
        public Guid EventTypeId { get; set; }
        public int DiscountPercent { get; set; }    // 100 = included
    }

    /// <summary>
    /// Something a season pass product grants its holder. One row per surface per scope: a
    /// discount on a given event type, on F&amp;B, on rentals, or a countable grant like buddy
    /// passes. Supersedes <see cref="SeasonPassEventTypePerk"/>, which is kept until the old
    /// table is dropped.
    /// </summary>
    public class SeasonPassBenefit
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid PassProductId { get; set; }

        /// <summary>'event' | 'concession' | 'rental' | 'buddy_pass'.</summary>
        public string BenefitType { get; set; } = null!;

        /// <summary>
        /// What this narrows to within its surface — the tenant_event_type id for 'event'.
        /// NULL means the whole surface ("10% off all F&amp;B").
        /// </summary>
        public Guid? ScopeId { get; set; }

        /// <summary>'percent' (DiscountValue is bps) or 'amount' (DiscountValue is cents).</summary>
        public string DiscountKind { get; set; } = "percent";
        public int DiscountValue { get; set; }

        /// <summary>Uses per season; NULL = unlimited. Set for countable grants like buddy passes.</summary>
        public int? Quantity { get; set; }

        /// <summary>A 100%-off discount: the surface is included in the pass rather than discounted.</summary>
        public bool IsIncluded => DiscountKind == "percent" && DiscountValue >= 10_000;

        /// <summary>
        /// This benefit's discount against a given price, clamped so it can never exceed the
        /// price (a $10-off benefit on a $6 item takes $6, not $10, and never mints a negative).
        /// </summary>
        public int DiscountFor(int priceCents)
        {
            if (priceCents <= 0) return 0;
            var raw = DiscountKind == "percent"
                ? (int)((long)priceCents * DiscountValue / 10_000L)
                : DiscountValue;
            return Math.Clamp(raw, 0, priceCents);
        }
    }

    public class SeasonPassPurchase
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid PurchaserUserId { get; set; }
        public Guid ProductId { get; set; }
        public Guid? WaiverSignatureId { get; set; }
        public string? StripePaymentIntentId { get; set; }
        // Set for direct charges: the tenant's connected account this pass was charged on.
        // NULL = platform charge. Drives refunds/finalization onto the right account.
        public string? StripeConnectedAccountId { get; set; }
        public int AmountCents { get; set; }
        public int ServiceChargeCents { get; set; }
        public string PaymentMethod { get; set; } = "stripe";
        public string Status { get; set; } = "pending";
        public string PurchaserEmail { get; set; } = null!;
        public string PurchaserName { get; set; } = null!;
        public Guid RedemptionToken { get; set; }
        public DateTime ValidFromDate { get; set; }
        public DateTime ValidToDate { get; set; }
        public int? CreditsRemaining { get; set; }
        public string? CancellationReason { get; set; }
        public DateTime? CancelledAt { get; set; }
        public Guid? CancelledByUserId { get; set; }
        public string? RefundNote { get; set; }
        public string? PhotoDataUrl { get; set; }
        // The person this pass admits, captured in the post-payment registration step. Distinct
        // from Purchaser*: one buyer can hold several passes for other riders (a parent buying
        // for their kids). NULL until registration completes.
        public string? HolderFirstName { get; set; }
        public string? HolderLastName { get; set; }
        public DateTime? HolderBirthdate { get; set; }
        // ── Stored ID/age verification of the HOLDER (Script0238) ────────────
        // Lives on the purchase, not only on users, because the holder frequently has no account
        // of their own (the parent-buying-for-a-kid case this class already documents above).
        // IdVerifiedDob is what the photo ID said, as against the self-reported HolderBirthdate.
        public DateTime? IdVerifiedAt { get; set; }
        public Guid? IdVerifiedByUserId { get; set; }
        public DateTime? IdVerifiedDob { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        /// <summary>
        /// A pass is only usable at the gate once it has a photo to check the holder's face
        /// against and, when the product requires a waiver, a signature. Checkout creates the row
        /// deliberately incomplete (payment first, holder details after), so this is what
        /// separates a paid-but-unregistered pass from a redeemable one.
        /// </summary>
        /// <remarks>
        /// Holder name is deliberately NOT part of this test even though registration requires it.
        /// Passes sold before the registration step have their holder backfilled from
        /// purchaser_name, which yields no last name for a single-word name — gating on it would
        /// turn away legacy holders who have both a photo and a signature on file. Any pass the
        /// new flow creates gets a name and photo in the same write, so nothing is lost.
        /// </remarks>
        public bool IsRegistered(bool productRequiresWaiver) =>
            !string.IsNullOrWhiteSpace(PhotoDataUrl)
            && (!productRequiresWaiver || WaiverSignatureId.HasValue);
    }

    public class SeasonPassPurchaseWithContext : SeasonPassPurchase
    {
        public string ProductName { get; set; } = null!;
        public string ProductKind { get; set; } = null!;
        public int? ProductTotalCredits { get; set; }
        public int[]? ProductValidDaysOfWeek { get; set; }
        public bool ProductRequiresWaiver { get; set; }

        /// <summary>Registration state resolved against this row's own product.</summary>
        public bool IsRegistered() => IsRegistered(ProductRequiresWaiver);
    }

    /// <summary>
    /// One holder's pass paired with the benefit it grants on a surface. Checkout works in these
    /// rather than in benefits alone: the discount is an entitlement OF A PASS, so a buyer holding
    /// three covering passes gets three grants and can discount three tickets, while one pass
    /// discounts one — no matter how many tickets are in the cart.
    /// </summary>
    public class SeasonPassBenefitGrant
    {
        public Guid PassPurchaseId { get; set; }
        public Guid PassProductId { get; set; }
        public string ProductName { get; set; } = null!;
        public string ProductKind { get; set; } = null!;
        public int? CreditsRemaining { get; set; }
        public SeasonPassBenefit Benefit { get; set; } = null!;
    }

    /// <summary>One admission's worth of a season pass. Anchored to EITHER an event (the
    /// classic case) or, for a walk-up track open on a day with nothing on the calendar, to
    /// CheckInDate. Never neither: the database enforces that with
    /// chk_season_pass_reservation_anchor.</summary>
    public class SeasonPassReservation
    {
        public Guid Id { get; set; }
        public Guid SeasonPassPurchaseId { get; set; }
        /// <summary>NULL on a no-event walk-up admission, where CheckInDate is the anchor instead.</summary>
        public Guid? EventId { get; set; }
        /// <summary>Tenant-local calendar date of a no-event walk-up admission. NULL on every
        /// event-anchored row, where the event's own schedule is the source of truth.</summary>
        public DateTime? CheckInDate { get; set; }
        public string Status { get; set; } = "reserved";    // reserved | checked_in | cancelled
        public DateTime ReservedAt { get; set; }
        public DateTime? CheckedInAt { get; set; }
        public DateTime? CancelledAt { get; set; }
    }

    public class SeasonPassReservationWithContext : SeasonPassReservation
    {
        // All three are null on a no-event walk-up row: there is no event to describe.
        public string? EventTitle { get; set; }
        public DateTime? EventStartsAt { get; set; }
        public DateTime? EventEndsAt { get; set; }
    }

    /// <summary>A reservation resolved to its event + the season-pass holder, for check-in gating.</summary>
    public class SeasonPassCheckInContext
    {
        public Guid ReservationId { get; set; }
        public Guid EventId { get; set; }

        // The ACCOUNT that bought the pass. Not necessarily the person it admits — a parent buys
        // passes for their kids — so this is only a valid key for waiver lookups when the pass
        // carries no holder signature of its own. See SeasonPassController.CheckIn.
        public Guid? HolderUserId { get; set; }
        public string? HolderEmail { get; set; }
        public string? HolderName { get; set; }

        // Registration state of the pass behind this reservation. The gate refuses check-in until
        // the holder is on file with a photo and (when the product needs one) a signature.
        // First name only — it's just to name the holder in the block message.
        public string? HolderFirstName { get; set; }

        // Presence only — the photo itself is a base64 blob up to ~2MB and the gate check just
        // needs to know it exists.
        public bool HasPhoto { get; set; }
        public Guid? WaiverSignatureId { get; set; }
        public bool ProductRequiresWaiver { get; set; }
    }
}
