using Services.Waivers;

namespace Services.Riders
{
    /// <summary>What the cashier typed for "another rider" on a Gate Sale (a friend, a son's friend).</summary>
    public sealed record OtherRiderInput(
        string? FirstName,
        string? LastName,
        DateOnly? Birthdate,
        string? Email,
        string? Phone,
        bool GuardianIsPayer = false,
        bool HasGuardianAccount = false,
        string? GuardianFirstName = null,
        string? GuardianLastName = null,
        string? GuardianEmail = null,
        string? GuardianPhone = null);

    /// <summary>Who a rider line's signing link goes to.</summary>
    public sealed record WaiverLinkRecipient(bool Guardian, string Email, string? Phone);

    /// <summary>
    /// Pure rules for Gate Sale rider lines (Script0309): what a new rider profile needs, who signs, where
    /// the signing link goes and how a line's waiver reads on the counter. Shared by the counter endpoints
    /// so the dialog, the link and the status chip can never disagree.
    /// </summary>
    public static class CounterRiderRules
    {
        public const string StatusNotRequired = "not_required";
        public const string StatusSigned = "signed";
        public const string StatusLinkSent = "link_sent";
        public const string StatusNeedsSignature = "needs_signature";

        /// <summary>The oldest birthdate the counter accepts (typos like 1902 instead of 2002).</summary>
        public const int MaxAgeYears = 110;

        public static bool IsValidEmail(string? email)
        {
            var e = email?.Trim();
            if (string.IsNullOrEmpty(e) || e.Length > 200 || e.Contains(' ')) return false;
            var at = e.IndexOf('@');
            return at > 0 && at == e.LastIndexOf('@') && e.IndexOf('.', at) > at + 1 && !e.EndsWith('.');
        }

        public static int PhoneDigits(string? phone) => (phone ?? string.Empty).Count(char.IsDigit);

        public static bool IsMinor(DateOnly birthdate, DateOnly today) => GuardianWaiverRule.IsMinorOn(birthdate, today);

        /// <summary>Why the counter can't build a profile from this input, or null when it can. An existing
        /// profile only needs a birthdate (it decides who signs) and, for a minor, a guardian.</summary>
        public static string? Validate(OtherRiderInput input, DateOnly today, bool existingProfile, string? payerEmail)
        {
            if (!existingProfile)
            {
                if (string.IsNullOrWhiteSpace(input.FirstName) || string.IsNullOrWhiteSpace(input.LastName))
                    return "Enter the rider's first and last name.";
                if (input.FirstName!.Trim().Length > 120 || input.LastName!.Trim().Length > 120)
                    return "That name is too long.";
            }
            if (input.Birthdate is not { } dob)
                return "Enter the rider's birthdate: it decides whether a parent or guardian has to sign.";
            if (dob > today || dob < today.AddYears(-MaxAgeYears))
                return "Enter a valid birthdate.";

            var email = input.Email?.Trim();
            if (!string.IsNullOrEmpty(email) && !IsValidEmail(email))
                return "That rider email doesn't look right.";
            if (!string.IsNullOrEmpty(input.Phone) && PhoneDigits(input.Phone) < 7)
                return "That rider phone number is too short.";
            if (!existingProfile && !string.IsNullOrEmpty(email) && SameEmail(email, payerEmail))
                return "That is the customer's own email. Add the ticket for the customer instead of another rider.";

            if (!IsMinor(dob, today))
            {
                // An adult signs for themselves, so the link needs their own address.
                if (!existingProfile && string.IsNullOrEmpty(email))
                    return "Riders 18 and over sign their own waiver: enter their email so they get their own profile and link.";
                return null;
            }

            // A minor: someone must sign as their parent or guardian.
            if (input.GuardianIsPayer || input.HasGuardianAccount) return null;
            if (string.IsNullOrWhiteSpace(input.GuardianFirstName) || string.IsNullOrWhiteSpace(input.GuardianLastName))
                return "Riders under 18 need a parent or guardian: enter their first and last name.";
            if (!IsValidEmail(input.GuardianEmail))
                return "Enter the parent or guardian's email: the signing link and their profile use it.";
            if (!string.IsNullOrEmpty(input.GuardianPhone) && PhoneDigits(input.GuardianPhone) < 7)
                return "That parent or guardian phone number is too short.";
            if (!string.IsNullOrEmpty(email) && SameEmail(email, input.GuardianEmail))
                return "The guardian's email is the rider's own email. Enter the parent or guardian's address.";
            return null;
        }

        /// <summary>A minor's link goes to their guardian, an adult's to themselves. Null when that person
        /// has no email on file (the counter then signs on the device instead).</summary>
        public static WaiverLinkRecipient? LinkRecipient(bool isMinor, string? riderEmail, string? riderPhone,
            string? guardianEmail, string? guardianPhone)
        {
            var email = (isMinor ? guardianEmail : riderEmail)?.Trim();
            if (!IsValidEmail(email)) return null;
            var phone = (isMinor ? guardianPhone : riderPhone)?.Trim();
            return new WaiverLinkRecipient(isMinor, email!, PhoneDigits(phone) >= 7 ? phone : null);
        }

        /// <summary>How a rider line's waiver reads on the counter.</summary>
        /// <param name="required">The gate requires a waiver for this ticket.</param>
        /// <param name="satisfied">The gate's waiver requirement is met (a valid, guardian-correct signature).</param>
        /// <param name="openRequestStatus">The line's latest signing link status (pending / sent / opened / signed), if any.</param>
        public static string LineStatus(bool required, bool satisfied, string? openRequestStatus)
        {
            if (!required) return StatusNotRequired;
            if (satisfied) return StatusSigned;
            return openRequestStatus is "pending" or "sent" or "opened" ? StatusLinkSent : StatusNeedsSignature;
        }

        /// <summary>The text message carrying a signing link. Kept short (one or two segments).</summary>
        public static string SmsBody(string venue, string riderFirstName, bool guardian, string link) =>
            guardian
                ? $"{venue}: please sign the waiver for {riderFirstName} as their parent or guardian before they ride: {link}"
                : $"{venue}: please sign your waiver before you ride: {link}";

        private static bool SameEmail(string? a, string? b) =>
            !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b)
            && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
