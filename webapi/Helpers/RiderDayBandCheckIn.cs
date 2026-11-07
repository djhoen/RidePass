using Services.Repositories.Data.WristbandData;

namespace webapi.Helpers
{
    /// <summary>
    /// The day band at a pass check-in (IRiderDayBands.ForCheckIn): the band the rider already wears that day,
    /// and the refusal when the track requires a band and there is none (nor one scanned with the admission).
    /// </summary>
    public sealed record RiderDayBandCheckIn(RiderDayBand? Worn, bool Required)
    {
        /// <summary>Who it is about (for a per-person message).</summary>
        public string? Name { get; init; }

        public const string RequiredCode = "band_required";
        public const string RequiredMessage =
            "A wristband is required to check in. Scan the band with the admission (Admit and band on the gate screen).";

        /// <summary>The structured refusal every check-in path returns, or null when the admission may go ahead.</summary>
        public Microsoft.AspNetCore.Mvc.IActionResult? Refusal =>
            Required ? new Services.Helpers.ApiResponses().BadRequestResult(RequiredMessage, new { code = RequiredCode }) : null;
    }
}
