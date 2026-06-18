using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Geo;
using Services.Helpers;
using Services.Repositories.Interfaces;
using webapi.Controllers.API.Data.Discover;

namespace webapi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [AllowAnonymous]
    public class DiscoverController : ControllerBase
    {
        private readonly IDiscoverRepository _discover;
        private readonly IGeoIpService _geoIp;
        private readonly IWebHostEnvironment _env;

        public DiscoverController(IDiscoverRepository discover, IGeoIpService geoIp, IWebHostEnvironment env)
        {
            _discover = discover;
            _geoIp = geoIp;
            _env = env;
        }

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
                HeroImageUrl = r.HeroImageUrl,
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
            [FromQuery] DateTime? toUtc,
            [FromQuery] string[]? eventTypeCodes,
            [FromQuery] Guid[]? tenantIds,
            [FromQuery] string[]? excludeCodes)
        {
            if ((lat.HasValue) != (lng.HasValue))
            {
                return new ApiResponses().BadRequestResult("Must supply both lat and lng, or neither.");
            }

            var rows = await _discover.SearchEvents(lat, lng, radiusKm, q, fromUtc, toUtc, eventTypeCodes, tenantIds, excludeCodes);
            var items = rows.Select(r => new EventDiscoverItem
            {
                EventId = r.EventId,
                TenantId = r.TenantId,
                TenantSubdomain = r.TenantSubdomain,
                TenantDisplayName = r.TenantDisplayName,
                TenantCity = r.TenantCity,
                TenantRegion = r.TenantRegion,
                TenantLogoUrl = r.TenantLogoUrl,
                Latitude = r.Latitude,
                Longitude = r.Longitude,
                DistanceKm = r.DistanceKm,
                Title = r.Title,
                StartsAtUtc = DateTime.SpecifyKind(r.StartsAtUtc, DateTimeKind.Utc),
                EndsAtUtc = DateTime.SpecifyKind(r.EndsAtUtc, DateTimeKind.Utc),
                LocationLabel = r.LocationLabel,
                EventTypeCode = r.EventTypeCode,
                EventTypeName = r.EventTypeName,
                EventTypeColor = r.EventTypeColor,
                ImageUrl = r.ImageUrl,
                EventTypeImageUrl = r.EventTypeImageUrl,
            });
            return new ApiResponses().OkResult(items);
        }

        // Selectable event types for the apex Events filter. `onlyCodes` restricts to
        // an allow-list; `excludeCodes` is a deny-list (the apex page hides private
        // bookings + lessons but offers every other type that has upcoming events).
        [HttpGet("EventTypes")]
        public async Task<IActionResult> ListEventTypes([FromQuery] string[]? onlyCodes, [FromQuery] string[]? excludeCodes)
        {
            var rows = await _discover.ListEventTypeOptions(onlyCodes, excludeCodes);
            var items = rows.Select(r => new EventTypeOption
            {
                Code = r.Code,
                Name = r.Name,
                Color = r.Color,
            });
            return new ApiResponses().OkResult(items);
        }

        // Resolve the caller's country + approximate coords from their IP. Drives
        // the apex Events page's US-vs-out-of-country branch and seeds the radius
        // center without a browser geolocation prompt. In non-Production a
        // `?debugCountry=` override makes the out-of-country flow testable from a
        // local/private IP (where the lookup can't resolve a real country).
        [HttpGet("GeoLocate")]
        public async Task<IActionResult> GeoLocate([FromQuery] string? debugCountry)
        {
            if (!_env.IsProduction() && !string.IsNullOrWhiteSpace(debugCountry))
            {
                return new ApiResponses().OkResult(new GeoLocateResult
                {
                    CountryCode = debugCountry.Trim().ToUpperInvariant(),
                });
            }

            var ip = ResolveClientIp();
            var geo = await _geoIp.Locate(ip);
            return new ApiResponses().OkResult(new GeoLocateResult
            {
                CountryCode = geo?.CountryCode,
                Latitude = geo?.Latitude,
                Longitude = geo?.Longitude,
            });
        }

        // Prefer the left-most X-Forwarded-For entry (the original client) since the
        // API runs behind nginx; fall back to the socket peer address.
        private string? ResolveClientIp()
        {
            var forwarded = Request.Headers["X-Forwarded-For"].ToString();
            if (!string.IsNullOrWhiteSpace(forwarded))
            {
                var first = forwarded.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(first)) return first;
            }
            return HttpContext.Connection.RemoteIpAddress?.ToString();
        }
    }
}
