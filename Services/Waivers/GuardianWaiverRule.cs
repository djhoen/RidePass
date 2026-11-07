namespace Services.Waivers
{
    /// <summary>
    /// Everything known about a rider's date of birth, in the places it can come from. Resolved
    /// in a fixed order by <see cref="GuardianWaiverRule.ResolveBirthdate"/>.
    /// </summary>
    /// <param name="VerifiedDob">What a photo ID said at the gate (ticket / pass id_verified_dob).</param>
    /// <param name="RecordedBirthdate">Captured for THIS ticket or pass (rider_birthdate / holder_birthdate).</param>
    /// <param name="SignatureBirthdate">The attendee birthdate written on the linked waiver signature.</param>
    /// <param name="AccountVerifiedDob">The buyer account's ID-verified DOB.</param>
    /// <param name="AccountBirthdate">The buyer account's self-reported birthdate.</param>
    /// <param name="AccountIsRider">Whether the buyer account is the rider. A parent buying for a kid
    /// is not, and their birthdate must never stand in for the kid's.</param>
    public sealed record RiderBirthdateFacts(
        DateOnly? VerifiedDob,
        DateOnly? RecordedBirthdate,
        DateOnly? SignatureBirthdate,
        DateOnly? AccountVerifiedDob,
        DateOnly? AccountBirthdate,
        bool AccountIsRider);

    /// <summary>
    /// The waiver signature presented as evidence. <paramref name="HasSignatureRow"/> is false for a
    /// legacy ticket that only carries an inline signature; then the ticket's own guardian name
    /// (<paramref name="LegacyGuardianName"/>) is the only record of who signed.
    /// </summary>
    public sealed record WaiverSignatureFacts(
        bool HasSignatureRow,
        bool SignedByParent,
        string? ParentName,
        string? SignerName,
        string? LegacyGuardianName);

    /// <summary>Outcome of the guardian rule for one rider on one day.</summary>
    /// <param name="BlockReason">Why the signature does not count, or null.</param>
    /// <param name="Age">Age on the check-in day, null when no birthdate is known.</param>
    /// <param name="IsMinor">Under 18 on the check-in day; null when no birthdate is known.</param>
    /// <param name="BirthdateUnknown">No birthdate anywhere. NOT a block: staff should check ID.</param>
    /// <param name="SignedByGuardian">The signature is a parent / guardian's (with a name).</param>
    /// <param name="GuardianName">That guardian's name.</param>
    public sealed record GuardianCheck(
        string? BlockReason,
        int? Age,
        bool? IsMinor,
        bool BirthdateUnknown,
        bool SignedByGuardian,
        string? GuardianName);

    /// <summary>
    /// The gate's minor / guardian rule, as pure functions so every check-in path and every
    /// screen that previews a check-in decide it the same way:
    ///
    /// A rider who is under 18 ON THE DAY OF CHECK-IN (by the tenant's local date) needs a waiver
    /// signed by a parent or guardian, recorded as signed_by_parent with the guardian's name. A
    /// minor's self-signed waiver blocks. An adult's own signature is enough. A rider with no
    /// birthdate anywhere is NOT treated as an adult silently: the check passes but says the
    /// birthdate is unknown so staff can verify it.
    ///
    /// Signing-time checks keep using WaiverPolicy.IsMinor (UTC today, unknown = adult); this is
    /// the admission-time rule.
    /// </summary>
    public static class GuardianWaiverRule
    {
        public const int AdultAge = 18;

        /// <summary>
        /// Whole years old on <paramref name="onDate"/>. The birthday counts from the day itself, so
        /// a rider is 18 on their 18th birthday. A 29 February birthday falls on 1 March in a
        /// non-leap year (the later of the two readings), so a leap-day minor is not treated as an
        /// adult a day early.
        /// </summary>
        public static int AgeOn(DateOnly birthdate, DateOnly onDate)
        {
            var age = onDate.Year - birthdate.Year;
            if (onDate.Month < birthdate.Month
                || (onDate.Month == birthdate.Month && onDate.Day < birthdate.Day))
                age--;
            return age;
        }

        public static bool IsMinorOn(DateOnly birthdate, DateOnly onDate) => AgeOn(birthdate, onDate) < AdultAge;

        /// <summary>
        /// Verified DOB, then the ticket / pass's own birthdate, then the signature's attendee
        /// birthdate, then the buyer account's (verified first) only when that account IS the rider.
        /// </summary>
        public static DateOnly? ResolveBirthdate(RiderBirthdateFacts f) =>
            f.VerifiedDob
            ?? f.RecordedBirthdate
            ?? f.SignatureBirthdate
            ?? (f.AccountIsRider ? f.AccountVerifiedDob ?? f.AccountBirthdate : null);

        /// <summary>
        /// Whether the buyer account is the rider: no rider name was captured (the account bought for
        /// itself), or the captured name matches the account's name.
        /// </summary>
        public static bool AccountIsRider(string? riderFirst, string? riderLast, string? accountFirst, string? accountLast)
        {
            if (string.IsNullOrWhiteSpace(riderFirst) && string.IsNullOrWhiteSpace(riderLast)) return true;
            return Same(riderFirst, accountFirst) && Same(riderLast, accountLast);
        }

        /// <summary>A guardian signature: the row says signed_by_parent with a guardian name, or a legacy
        /// ticket without a row carries a guardian name of its own.</summary>
        public static bool IsGuardianSigned(WaiverSignatureFacts s) =>
            s.HasSignatureRow
                ? s.SignedByParent && !string.IsNullOrWhiteSpace(s.ParentName)
                : !string.IsNullOrWhiteSpace(s.LegacyGuardianName);

        public static string MinorBlockMessage(string? riderName) =>
            $"{(string.IsNullOrWhiteSpace(riderName) ? "This rider" : riderName.Trim())} is under {AdultAge}. "
            + "A parent or guardian has to sign their waiver.";

        /// <summary>
        /// Apply the rule to a signature that is otherwise valid for this rider and event.
        /// </summary>
        public static GuardianCheck Evaluate(DateOnly? birthdate, DateOnly onDate, WaiverSignatureFacts signature,
            string? riderName)
        {
            var byGuardian = IsGuardianSigned(signature);
            var guardian = byGuardian
                ? (signature.HasSignatureRow ? signature.ParentName : signature.LegacyGuardianName)?.Trim()
                : null;

            if (birthdate is null)
                return new GuardianCheck(null, null, null, BirthdateUnknown: true, byGuardian, guardian);

            var age = AgeOn(birthdate.Value, onDate);
            var minor = age < AdultAge;
            if (minor && !byGuardian)
                return new GuardianCheck(MinorBlockMessage(riderName), age, true, false, false, null);

            // TODO(gate item 4): an ADULT whose only waiver was signed by a guardian while they were
            // a minor. Allowed for now; item 4 decides whether that signature still counts.
            return new GuardianCheck(null, age, minor, false, byGuardian, guardian);
        }

        /// <summary>
        /// The email a guardian signing link should default to: the buyer's, when the buyer is
        /// someone other than the rider (a parent buying for a kid). Null when the rider bought it
        /// themselves, since then no guardian is on file and staff must enter one.
        /// </summary>
        public static string? SuggestGuardianEmail(string? riderFirst, string? riderLast, string? buyerName, string? buyerEmail)
        {
            if (string.IsNullOrWhiteSpace(buyerEmail)) return null;
            var rider = $"{riderFirst?.Trim()} {riderLast?.Trim()}".Trim();
            if (rider.Length == 0) return null;
            return Same(rider, buyerName) ? null : buyerEmail.Trim();
        }

        private static bool Same(string? a, string? b) =>
            string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

        public static DateOnly? ToDateOnly(DateTime? d) => d.HasValue ? DateOnly.FromDateTime(d.Value) : null;
    }
}
