using Services.Repositories.Data.WaiverData;
using Services.Waivers;

namespace webapi.Helpers
{
    /// <summary>
    /// Who a signing link is for and how it must be signed, worked out ONCE for both the public page
    /// (GET SignRequest/{token}, which shows it up front) and the signing itself (SignByToken, which
    /// enforces it), so the two can never disagree.
    /// </summary>
    /// <param name="Target">The gate target (Script0304) when the link was sent for a ticket / pass.</param>
    /// <param name="RiderIdentity">The rider an adult-waiver link with no credential signs AS (Script0306).</param>
    /// <param name="TargetMissing">The link names a ticket / pass that no longer exists.</param>
    /// <param name="RiderFirstName">The rider's first name as the venue holds it; null when unknown or blank.</param>
    /// <param name="RiderBirthdate">The birthdate the server decides by; null when it needs the form's.</param>
    /// <param name="KnownMinor">The server already knows whether a parent / guardian must sign; null when
    /// it decides from the birthdate typed on the form.</param>
    public sealed record SignRequestMode(
        WaiverSignTarget? Target,
        SignRequestRider? RiderIdentity,
        bool TargetMissing,
        string? RiderFirstName,
        string? RiderLastName,
        DateTime? RiderBirthdate,
        bool? KnownMinor)
    {
        public static readonly SignRequestMode Untargeted = new(null, null, false, null, null, null, null);

        /// <summary>"guardian" | "adult" | null (the page asks for the birthdate).</summary>
        public string? SignAs => KnownMinor is null ? null : KnownMinor.Value ? "guardian" : "adult";

        /// <summary>The rider's full name, or null when either part is unknown (the page's own copy applies).</summary>
        public string? RiderName => RiderFirstName is not null && RiderLastName is not null
            ? $"{RiderFirstName} {RiderLastName}" : null;

        /// <summary>Blank-or-missing is "unknown", trimmed otherwise: the same test everywhere.</summary>
        public static string? Clean(string? name) => string.IsNullOrWhiteSpace(name) ? null : name.Trim();

        /// <summary>
        /// For a ticket / pass target (a gate signing link, or a waiver pushed to a kiosk): the gate's own view
        /// of that rider decides: their name, the birthdate the gate uses, and its minor verdict, else that
        /// birthdate. One rule for both, so a kiosk signs exactly as a link would.
        /// </summary>
        public static SignRequestMode ForTarget(WaiverSignTarget target)
        {
            var dob = target.Decision.Birthdate is { } tb ? tb.ToDateTime(TimeOnly.MinValue) : (DateTime?)null;
            bool? minor = target.Decision is { RiderAudience: true, IsMinor: bool m } ? m
                : dob is { } b ? WaiverPolicy.IsMinor(b) : null;
            return new SignRequestMode(target, null, false, Clean(target.FirstName), Clean(target.LastName), dob, minor);
        }
    }
}
