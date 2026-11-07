using Services.Helpers;
using Services.Waivers;

namespace webapi.Helpers
{
    /// <summary>
    /// A waiver-gate refusal in a form a caller can act on. Every refusal still carries the
    /// human-readable reason in "error"; a birthdate-check refusal ('required' mode) also carries
    /// data.code = "birthdate_unverified", the rider's name, and the token + kind of the existing
    /// verify endpoint that clears it (Redemption/Order/{token}/VerifyId for a ticket,
    /// SeasonPass/Pass/{token}/VerifyId for a pass), so a screen can offer "Verify ID" in place.
    /// </summary>
    public static class GateBlockResult
    {
        public const string BirthdateUnverified = "birthdate_unverified";

        /// <summary>The structured part of a refusal, or null when it isn't a birthdate-check one.</summary>
        public static object? Details(WaiverGateDecision d, string verifyKind, Guid? verifyToken, Guid? purchaseId) =>
            d.BirthdateBlocked
                ? new
                {
                    code = BirthdateUnverified,
                    riderName = d.RiderName,
                    lastCheckedLocalDate = d.IdVerifiedLocalDate,
                    verifyKind,          // "ticket" | "season_pass"
                    verifyToken,
                    purchaseId,
                }
                : null;

        public static Microsoft.AspNetCore.Mvc.IActionResult Result(WaiverGateDecision d, string verifyKind,
            Guid? verifyToken, Guid? purchaseId) =>
            Details(d, verifyKind, verifyToken, purchaseId) is { } details
                ? new ApiResponses().BadRequestResult(d.BlockReason!, details)
                : new ApiResponses().BadRequestResult(d.BlockReason!);

        /// <summary>Just the code, for per-item results (AdmitBatch).</summary>
        public static string? Code(WaiverGateDecision d) => d.BirthdateBlocked ? BirthdateUnverified : null;
    }
}
