namespace Services.Repositories.Data.BikeShopData
{
    // Rentals on the unified shop catalog. A rental books catalog variants for a half-open window
    // [StartsAt, EndsAt); booking reserves CAPACITY by window overlap, and physical stock moves at
    // checkout/return. The fee is a normal PaymentIntent (or cash); the deposit is a separate
    // manual-capture hold, charged only for damage.

    public class ShopRental
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid? RenterUserId { get; set; }
        public string? RenterName { get; set; }
        public string? RenterEmail { get; set; }
        public string? RenterPhone { get; set; }
        public Guid? WaiverSignatureId { get; set; }
        public DateTime StartsAt { get; set; }
        public DateTime EndsAt { get; set; }
        public string Status { get; set; } = "pending";   // pending|paid|out|returned|damaged|cancelled|failed
        public int AmountCents { get; set; }
        public int TaxCents { get; set; }
        public int TotalCents { get; set; }
        /// <summary>
        /// Full tenant service charge on the rental subtotal (deposit excluded), frozen at booking.
        /// What RidePass is owed; the renter-paid share of it is already inside TotalCents.
        /// </summary>
        public int ServiceChargeCents { get; set; }
        /// <summary>How many riders must each sign the waiver before check-out.</summary>
        public int RidersRequired { get; set; } = 1;
        /// <summary>
        /// Damage waiver fee charged on this rental, frozen at booking. Greater than zero IS the
        /// record that the waiver was bought; DepositCents landing at 0 is a consequence of it,
        /// not evidence for it (plenty of gear carries no deposit at all).
        /// </summary>
        public int InsuranceCents { get; set; }
        /// <summary>What the renter was told they were buying, frozen so a later rename of the
        /// tenant's label doesn't rewrite an old receipt.</summary>
        public string? InsuranceLabelSnapshot { get; set; }
        public int DepositCents { get; set; }
        public string? DepositPiId { get; set; }
        public int DepositCapturedCents { get; set; }
        public string PaymentMethod { get; set; } = "stripe";
        public string? StripePaymentIntentId { get; set; }
        public string? StripeConnectedAccountId { get; set; }
        public int? OrderNumber { get; set; }
        public Guid? SoldByUserId { get; set; }
        public Guid ReceiptToken { get; set; }
        public DateTime? CheckedOutAt { get; set; }
        public DateTime? ReturnedAt { get; set; }
        public string? ConditionNotes { get; set; }
        public Guid? EventId { get; set; }
        /// <summary>Credential for the public signing page: the renter signs the agreement and
        /// waiver from an emailed link instead of at the counter.</summary>
        public Guid SignatureRequestToken { get; set; }
        public DateTime? SignatureRequestSentAt { get; set; }

        // Staff-applied discount snapshot (Script0257), set when a rental is discounted at the gate
        // counter as part of a lesson booking.
        public int DiscountCents { get; set; }
        public Guid? DiscountPresetId { get; set; }
        public string? DiscountLabel { get; set; }
        public Guid? DiscountAuthorizedByUserId { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class ShopRentalLine
    {
        public Guid Id { get; set; }
        public Guid RentalId { get; set; }
        public Guid VariantId { get; set; }
        public Guid? ItemId { get; set; }
        public int Quantity { get; set; }
        public string NameSnapshot { get; set; } = null!;
        public string? VariantLabel { get; set; }
        public int DailyRateCentsFrozen { get; set; }
        public int DepositCentsFrozen { get; set; }
        public int LineAmountCents { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class ShopRentalWithLines : ShopRental
    {
        public List<ShopRentalLine> Lines { get; set; } = new();
    }

    /// <summary>A shop_lesson_rentable row joined to its variant + product: a bike offered with a
    /// lesson, with the effective info the lesson config and checkout need.</summary>
    public class LessonRentableInfo
    {
        public Guid VariantId { get; set; }
        public int? PriceCentsOverride { get; set; }
        public string ProductName { get; set; } = null!;
        public string? Description { get; set; }
        public string? ImageUrl { get; set; }
        public string? Size { get; set; }
        public string? Color { get; set; }
        public string? Gender { get; set; }
        public int? DailyRateCents { get; set; }
        public int DepositCents { get; set; }
        public string TrackingKind { get; set; } = "pool";
        public bool IsActive { get; set; }
    }

    /// <summary>A rider who has signed the waiver for a rental.</summary>
    public class RentalSignerInfo
    {
        public Guid SignatureId { get; set; }
        public string? RiderName { get; set; }
        public bool SignedByParent { get; set; }
        public string? ParentName { get; set; }
        public DateTime SignedAtUtc { get; set; }
    }
    /// <summary>
    /// One entry in a rental's staff note thread (Script0248). Append-only and internal: nothing
    /// here is shown to the renter. Distinct from ConditionNotes, which is the single
    /// how-it-came-back record written at return.
    /// </summary>
    public class ShopRentalNote
    {
        public Guid Id { get; set; }
        public Guid RentalId { get; set; }
        public string Body { get; set; } = null!;
        public Guid? CreatedByUserId { get; set; }
        /// <summary>Author's display name, resolved via join; null if the account is gone.</summary>
        public string? CreatedByName { get; set; }
        public DateTime CreatedAt { get; set; }
    }

}
