using Services.Repositories.Data.WristbandData;
using Services.Riders;
using Services.Waivers;

namespace webapi.Helpers
{
    /// <summary>
    /// What a day-band check or write came to. A passed check carries everything the write needs (the
    /// action, the clean code, the business date, the rider, the band worn now and the replace reason), so the
    /// write runs no checks of its own. A failed one carries the Error, and the gate's decision when the gate
    /// itself blocks the rider (so the caller can return the structured GateBlockResult).
    /// </summary>
    public sealed record RiderDayBandResult(RiderDayBand? Band, string? Error, WaiverGateDecision? GateBlock,
        RiderPhotoKey? Rider, RiderDayBand? Current)
    {
        public bool Ok => Error is null;
        public DayBandAction? Action { get; init; }
        public string? Code { get; init; }
        public DateOnly Date { get; init; }
        public string? ReplaceReason { get; init; }

        public static RiderDayBandResult Fail(string error, RiderPhotoKey? rider = null, RiderDayBand? current = null) =>
            new(null, error, null, rider, current);
    }
}
