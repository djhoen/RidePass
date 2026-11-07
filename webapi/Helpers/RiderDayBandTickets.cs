using Services.Repositories.Data.PaymentData;
using Services.Repositories.Data.WristbandData;

namespace webapi.Helpers
{
    /// <summary>
    /// The day bands for a set of tickets admitted together on one business date (an order), read once
    /// (IRiderDayBands.ForTickets): each ticket's rider, the band each rider wears that day, and each ticket's
    /// check-in rule. Band checks for the same people reuse <see cref="Worn"/> instead of reading again.
    /// </summary>
    public sealed class RiderDayBandTickets
    {
        public DateOnly Date { get; init; }
        public Dictionary<Guid, RiderDayBandSubject> Subjects { get; } = new();
        public Dictionary<(string, string), RiderDayBand> Worn { get; init; } = new();
        public Dictionary<Guid, RiderDayBandCheckIn> CheckIns { get; } = new();

        public RiderDayBand? WornBy(RiderDayBandSubject subject) =>
            Worn.GetValueOrDefault((subject.Rider.OwnerKey, subject.Rider.RiderKey));
    }
}
