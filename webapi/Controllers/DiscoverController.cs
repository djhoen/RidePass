using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Interfaces;
using webapi.Controllers.API.Data.Discover;

namespace webapi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    [AllowAnonymous]
    public class DiscoverController : ControllerBase
    {
        private readonly IDiscoverRepository _discover;

        public DiscoverController(IDiscoverRepository discover) => _discover = discover;

        [HttpGet("Tracks")]
        public async Task<IActionResult> SearchTracks(
            [FromQuery] double? lat,
            [FromQuery] double? lng,
            [FromQuery] double? radiusKm,
            [FromQuery] string? q)
        {
            if ((lat.HasValue) != (lng.HasValue))
            {
                return new ApiResponses().BadRequestResult("Must supply both lat and lng, or neither.");
            }

            var rows = await _discover.SearchTracks(lat, lng, radiusKm, q);
            var items = rows.Select(r => new TrackDiscoverItem
            {
                TenantId = r.TenantId,
                Subdomain = r.Subdomain,
                DisplayName = r.DisplayName,
                AddressLine = r.AddressLine,
                City = r.City,
                Region = r.Region,
                PostalCode = r.PostalCode,
                Country = r.Country,
                Latitude = r.Latitude,
                Longitude = r.Longitude,
                DistanceKm = r.DistanceKm,
                UpcomingEventsCount = r.UpcomingEventsCount,
            });
            return new ApiResponses().OkResult(items);
        }

        [HttpGet("Events")]
        public async Task<IActionResult> SearchEvents(
            [FromQuery] double? lat,
            [FromQuery] double? lng,
            [FromQuery] double? radiusKm,
            [FromQuery] string? q,
            [FromQuery] DateTime? fromUtc,
            [FromQuery] DateTime? toUtc)
        {
            if ((lat.HasValue) != (lng.HasValue))
            {
                return new ApiResponses().BadRequestResult("Must supply both lat and lng, or neither.");
            }

            var rows = await _discover.SearchEvents(lat, lng, radiusKm, q, fromUtc, toUtc);
            var items = rows.Select(r => new EventDiscoverItem
            {
                EventId = r.EventId,
                TenantId = r.TenantId,
                TenantSubdomain = r.TenantSubdomain,
                TenantDisplayName = r.TenantDisplayName,
                TenantCity = r.TenantCity,
                TenantRegion = r.TenantRegion,
                Latitude = r.Latitude,
                Longitude = r.Longitude,
                DistanceKm = r.DistanceKm,
                Title = r.Title,
                StartsAtUtc = DateTime.SpecifyKind(r.StartsAtUtc, DateTimeKind.Utc),
                EndsAtUtc = DateTime.SpecifyKind(r.EndsAtUtc, DateTimeKind.Utc),
                LocationLabel = r.LocationLabel,
                EventTypeName = r.EventTypeName,
                EventTypeColor = r.EventTypeColor,
            });
            return new ApiResponses().OkResult(items);
        }
    }
}
