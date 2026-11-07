using Services.Helpers;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Data.UserData;
using Services.Riders;
using Services.Waivers;

namespace webapi.Helpers
{
    /// <summary>
    /// Whether a rider account's own signature still counts for them, shared by the account sign
    /// paths (app status / sign and the counter). A guardian's signature, or one made before the
    /// rider's 18th birthday, stops counting once they are 18 (the gate's adult rule), so these paths
    /// treat it as unsigned and let the adult sign again (WaiverRepository.Sign keeps the old row).
    /// </summary>
    public static class AccountWaiverStatus
    {
        /// <summary>The account's birthdate as the gate uses it: the ID-verified one, else the self-reported one.</summary>
        public static DateOnly? Birthdate(User? account) =>
            GuardianWaiverRule.ToDateOnly(account?.IdVerifiedDob ?? account?.Birthdate);

        /// <summary>
        /// Minor on the tenant's local today, from the same birthdate source the gate uses. The account
        /// sign paths (app and counter) decide "a parent must sign" with this, so they can never disagree
        /// with the gate (e.g. an ID check showing the rider is 17, or the day of the 18th birthday at a
        /// track whose date is not yet UTC's). No birthdate: not a minor (as before).
        /// </summary>
        public static bool IsMinorToday(User? account, string? timezone, DateTime utcNow) =>
            Birthdate(account) is { } b && GuardianWaiverRule.IsMinorOn(b, TenantClock.LocalDate(timezone, utcNow));

        public static bool IsMinorEraForAdult(User? account, RiderWaiverSignature? sig, string? timezone, DateTime utcNow)
        {
            if (account is null || sig is null) return false;
            var birthdate = Birthdate(account);
            var facts = new WaiverSignatureFacts(true, sig.SignedByParent, sig.ParentName, sig.SignerName, null,
                IdVerificationWindow.VerifiedLocalDate(sig.SignedAt, TenantClock.Resolve(timezone)));
            return GuardianWaiverRule.IsMinorEraForAdult(birthdate, TenantClock.LocalDate(timezone, utcNow), facts);
        }
    }
}
