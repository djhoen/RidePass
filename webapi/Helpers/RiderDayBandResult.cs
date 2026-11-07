using Services.Repositories.Data.WristbandData;
using Services.Riders;
using Services.Waivers;

namespace webapi.Helpers
{
    /// <summary>
    /// What a day-band check or issue came to: the band (issued now, already worn, or the replacement), or why
    /// not: an Error, and the gate's decision when the gate itself blocks the rider (so the caller can return
    /// the structured GateBlockResult).
    /// </summary>
    public sealed record RiderDayBandResult(RiderDayBand? Band, string? Error, WaiverGateDecision? GateBlock,
        RiderPhotoKey? Rider, RiderDayBand? Current)
    {
        public bool Ok => Error is null;
        public static RiderDayBandResult Fail(string error, RiderPhotoKey? rider = null, RiderDayBand? current = null) =>
            new(null, error, null, rider, current);
    }
}
