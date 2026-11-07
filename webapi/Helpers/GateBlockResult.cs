using Services.Helpers;
using Services.Waivers;

namespace webapi.Helpers
{
    /// <summary>
    /// A waiver-gate refusal in a form a caller can act on. Every refusal still carries the
    /// human-readable reason in "error"; two kinds also carry structured data:
    /// - "birthdate_unverified" ('required' birthdate check): the rider's name and the token + kind of
    ///   the existing verify endpoint that clears it (Redemption/Order/{token}/VerifyId for a ticket,
    ///   SeasonPass/Pass/{token}/VerifyId for a pass), so a screen can offer "Verify ID" in place;
    /// - "adult_waiver_needed" (turned 18 since their waiver): the rider's name, their 18th birthday,
    ///   their own email when known, and what Redemption/WaiverSignRequest needs (kind, purchaseId,
    ///   eventId) so a screen can offer "Send signing link" to the rider;
    /// - "photo_needed" ('required' rider photo, none on file): the rider's name and what
    ///   Redemption/RiderPhoto needs (photoKind, purchaseId) so a screen can offer "Take photo".
    /// </summary>
    public static class GateBlockResult
    {
        public const string BirthdateUnverified = "birthdate_unverified";
        public const string AdultWaiverNeeded = "adult_waiver_needed";
        public const string PhotoNeeded = "photo_needed";

        /// <summary>The structured part of a refusal, or null when it isn't one of the coded kinds.</summary>
        public static object? Details(WaiverGateDecision d, string verifyKind, Guid? verifyToken, Guid? purchaseId,
            Guid? eventId = null)
        {
            if (d.BirthdateBlocked)
                return new
                {
                    code = BirthdateUnverified,
                    riderName = d.RiderName,
                    lastCheckedLocalDate = d.IdVerifiedLocalDate,
                    verifyKind,          // "ticket" | "season_pass"
                    verifyToken,
                    purchaseId,
                };
            if (d.AdultWaiverNeeded && d.BlockReason is not null)
                return new
                {
                    code = AdultWaiverNeeded,
                    riderName = d.RiderName,
                    turnedEighteenOn = d.TurnedEighteenOn,
                    riderEmail = d.RiderEmail,
                    signKind = verifyKind,   // "ticket" | "season_pass", as Redemption/WaiverSignRequest takes it
                    purchaseId,
                    eventId,
                };
            if (d.PhotoBlocked)
                return new
                {
                    code = PhotoNeeded,
                    riderName = d.RiderName,
                    photoKind = verifyKind,  // "ticket" | "season_pass", as Redemption/RiderPhoto takes it
                    purchaseId,
                };
            return null;
        }

        public static Microsoft.AspNetCore.Mvc.IActionResult Result(WaiverGateDecision d, string verifyKind,
            Guid? verifyToken, Guid? purchaseId, Guid? eventId = null) =>
            Details(d, verifyKind, verifyToken, purchaseId, eventId) is { } details
                ? new ApiResponses().BadRequestResult(d.BlockReason!, details)
                : new ApiResponses().BadRequestResult(d.BlockReason!);

        /// <summary>Just the code, for per-item results (AdmitBatch).</summary>
        public static string? Code(WaiverGateDecision d) =>
            d.BirthdateBlocked ? BirthdateUnverified
            : d.AdultWaiverNeeded && d.BlockReason is not null ? AdultWaiverNeeded
            : d.PhotoBlocked ? PhotoNeeded
            : null;
    }
}
