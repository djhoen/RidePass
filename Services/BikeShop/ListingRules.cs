namespace Services.BikeShop
{
    /// <summary>
    /// The decisions behind used &amp; ex-rental listings (Script0302), pure so they can be pinned
    /// down without a database and so the admin tab, the public store, the register and the
    /// repository's own transactions cannot drift on them: which units may be listed, what a
    /// listing's state is, when an online reservation still holds the bike, and what a listing
    /// is called by default.
    /// </summary>
    public static class ListingRules
    {
        /// <summary>The shop_item status of a unit on the sales floor.</summary>
        public const string ForSale = "for_sale";

        /// <summary>How long an online checkout holds the bike before the next buyer may take it.</summary>
        public const int ReservationMinutes = 20;

        /// <summary>Asking prices above this are a typo, not a bike.</summary>
        public const int MaxAskingPriceCents = 100_000_000;

        public static readonly string[] Grades = { "excellent", "good", "fair" };

        public static bool IsValidGrade(string? grade) => grade is not null && Grades.Contains(grade);

        public static bool IsValidCondition(string? condition) => condition is "new" or "used";

        /// <summary>A unit may be listed from the fleet (available), the bench (maintenance) or
        /// retirement (retired): the three states in which nobody else has a claim on it.</summary>
        public static bool CanList(string? itemStatus) => itemStatus is "available" or "maintenance" or "retired";

        public static string CannotListReason(string? itemStatus) => itemStatus switch
        {
            "rented_out" => "That unit is out on a rental. Check it in before listing it.",
            "sold" => "That unit has already been sold.",
            ForSale => "That unit is already listed for sale.",
            _ => "That unit can't be listed right now.",
        };

        /// <summary>The register sells a unit from the fleet or from the sales floor; never one
        /// that is out, sold, on the bench or retired.</summary>
        public static bool IsSellableAtRegister(string? itemStatus) => itemStatus is "available" or ForSale;

        /// <summary>Null when the asking price is acceptable, else the message for the 400.</summary>
        public static string? ValidatePrice(int askingPriceCents)
        {
            if (askingPriceCents <= 0) return "Enter an asking price above $0.";
            if (askingPriceCents > MaxAskingPriceCents) return "That asking price is more than $1,000,000; check the amount.";
            return null;
        }

        /// <summary>"M", "M / Red", "M / Red / Women's": the same label the register prints.</summary>
        public static string VariantLabel(string? size, string? color, string? gender) =>
            string.Join(" / ", new[] { size, color, gender }
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!.Trim()));

        /// <summary>"{product name} {variant label} · {unit label}", skipping whichever parts are blank.</summary>
        public static string DefaultTitle(string productName, string? variantLabel, string? unitLabel)
        {
            var head = string.IsNullOrWhiteSpace(variantLabel)
                ? productName.Trim()
                : $"{productName.Trim()} {variantLabel.Trim()}";
            return string.IsNullOrWhiteSpace(unitLabel) ? head : $"{head} · {unitLabel.Trim()}";
        }

        /// <summary>A sale in one of these states holds nothing: it can never be paid.</summary>
        public static bool IsDeadSaleStatus(string? saleStatus) =>
            saleStatus is "cancelled" or "failed" or "abandoned" or "refunded";

        /// <summary>
        /// A reservation is live when it points at a sale that is not dead AND was taken less than
        /// <see cref="ReservationMinutes"/> ago, or when that sale is already paid (the unit is
        /// about to read 'sold'). A reservation whose sale row is gone is not live.
        /// </summary>
        public static bool IsReservationLive(Guid? reservedSaleId, DateTime? reservedAtUtc, string? reservedSaleStatus, DateTime nowUtc)
        {
            if (reservedSaleId is null || reservedAtUtc is null) return false;
            if (reservedSaleStatus is null || IsDeadSaleStatus(reservedSaleStatus)) return false;
            if (reservedSaleStatus == "paid") return true;
            return reservedAtUtc.Value > nowUtc.AddMinutes(-ReservationMinutes);
        }

        public static DateTime? ReservationExpiresAtUtc(DateTime? reservedAtUtc) =>
            reservedAtUtc?.AddMinutes(ReservationMinutes);

        /// <summary>
        /// active | reserved | sold | unlisted. Precedence: an unlisted row is unlisted whatever the
        /// unit did later; a sold unit is sold; a unit that is no longer on the floor for any other
        /// reason (an admin moved it, a refund returned it to the fleet) reads unlisted; otherwise
        /// the listing is active, or reserved while an online checkout holds it.
        /// </summary>
        public static string State(DateTime? unlistedAtUtc, string? itemStatus, bool reservationLive)
        {
            if (unlistedAtUtc is not null) return "unlisted";
            if (itemStatus == "sold") return "sold";
            if (itemStatus != ForSale) return "unlisted";
            return reservationLive ? "reserved" : "active";
        }

        /// <summary>
        /// Where a unit goes when its listing is taken down: back to the status it was listed from.
        /// Anything else (null on an old row, or a value that is not a listable status) reads as
        /// 'available', the rental fleet.
        /// </summary>
        public static string RestoreStatusOnUnlist(string? priorStatus) =>
            CanList(priorStatus) ? priorStatus! : "available";

        /// <summary>Refusal copy for listing a unit with a current or future rental booked on it,
        /// naming the booking's start date in the tenant's own zone.</summary>
        public static string RentalBookedMessage(DateTime rentalStartsAtUtc, string? timezoneId)
        {
            var utc = DateTime.SpecifyKind(rentalStartsAtUtc, DateTimeKind.Utc);
            var local = TimeZoneInfo.ConvertTimeFromUtc(utc, Services.Helpers.TenantClock.Resolve(timezoneId));
            var date = local.ToString("MMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture);
            return $"This bike is booked for a rental on {date}. Move or cancel that booking first.";
        }

        // ── An online hold, as the buyer sees it ─────────────────────────────────
        public const string HeldByYou = "held_by_you";
        public const string HeldByOther = "held_by_other";

        /// <summary>Whose checkout holds the bike, from the asking rider's point of view.</summary>
        public static string HoldReason(Guid? reservedBuyerUserId, Guid askingUserId) =>
            reservedBuyerUserId is Guid buyer && buyer == askingUserId ? HeldByYou : HeldByOther;

        /// <summary>A PaymentIntent the buyer can still finish with its client secret: it is waiting
        /// on them, not processing, settled or cancelled.</summary>
        public static bool IsResumable(string? paymentIntentStatus) =>
            paymentIntentStatus is "requires_payment_method" or "requires_confirmation" or "requires_action";

        /// <summary>The asking price the listing dialog prefills: acquired cost rounded UP to the
        /// nearest $5, or null when the cost is unknown (the field starts blank).</summary>
        public static int? SuggestedAskingPriceCents(int? acquiredCostCents)
        {
            if (acquiredCostCents is null || acquiredCostCents.Value <= 0) return null;
            return (acquiredCostCents.Value + 499) / 500 * 500;
        }
    }
}
