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
        public static bool IsMinorEraForAdult(User? account, RiderWaiverSignature? sig, string? timezone, DateTime utcNow)
        {
            if (account is null || sig is null) return false;
            var birthdate = GuardianWaiverRule.ToDateOnly(account.IdVerifiedDob ?? account.Birthdate);
            var facts = new WaiverSignatureFacts(true, sig.SignedByParent, sig.ParentName, sig.SignerName, null,
                IdVerificationWindow.VerifiedLocalDate(sig.SignedAt, TenantClock.Resolve(timezone)));
            return GuardianWaiverRule.IsMinorEraForAdult(birthdate, TenantClock.LocalDate(timezone, utcNow), facts);
        }
    }
}
