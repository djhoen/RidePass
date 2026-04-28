using Services.Repositories.Data.DiscoverData;

namespace Services.Repositories.Interfaces
{
    public interface IDiscoverRepository
    {
        Task<List<TrackDiscoverRow>> SearchTracks(double? lat, double? lng, double? radiusKm, string? q, int limit = 50);
        Task<List<EventDiscoverRow>> SearchEvents(double? lat, double? lng, double? radiusKm, string? q,
            DateTime? fromUtc, DateTime? toUtc, int limit = 100);
    }
}
