using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.BikeShop;
using Services.Helpers;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    // The bike shop's daily sheet: every rental and reservation for one tenant-local business day,
    // grouped by the program it was booked through (the shop's own rentals, an event type such as
    // Camp or After School, a package such as Find Your Ride), with phase and counts derived here
    // so the counter and the printout agree on what "due back today" means.
    //
    // Separate from BikeShopRentalController so the day read model has its own repository and
    // can grow without touching the booking paths.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = TenantPermissions.Policy.ShopCounter)]
    public class BikeShopDayController : ControllerBase
    {
        private readonly IShopDayRepository _days;
        private readonly ITenantContext _tenantContext;

        public BikeShopDayController(IShopDayRepository days, ITenantContext tenantContext)
        {
            _days = days;
            _tenantContext = tenantContext;
        }

        private Guid TenantId => _tenantContext.TenantId;

        /// <summary>
        /// GET api/BikeShopDay?date=yyyy-MM-dd&amp;includeCancelled=false. The date is a tenant-local
        /// calendar day; omitted, it is today at the track. Times in the response are UTC instants
        /// and <c>timezone</c> says which zone the day boundaries were drawn in.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] string? date = null, [FromQuery] bool includeCancelled = false)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");

            var tzId = _tenantContext.Tenant.Timezone;
            DateOnly day;
            if (string.IsNullOrWhiteSpace(date))
            {
                day = TenantClock.LocalDate(tzId, DateTime.UtcNow);
            }
            else if (!DateOnly.TryParseExact(date.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day))
            {
                return new ApiResponses().BadRequestResult("date must be yyyy-MM-dd.");
            }

            var (dayStartUtc, dayEndUtc) = TenantClock.DayRangeUtc(tzId, day);
            var raw = await _days.GetDay(TenantId, dayStartUtc, dayEndUtc, includeCancelled);

            // Echo the zone the boundaries were actually computed in: a blank or unknown tenant zone
            // reads as UTC in TenantClock, and the client should format times the same way.
            var timezone = TenantClock.Resolve(tzId).Equals(TimeZoneInfo.Utc) ? "UTC" : tzId!;
            var sheet = ShopDayMath.Build(day, timezone, dayStartUtc, dayEndUtc, raw);
            return new ApiResponses().OkResult(sheet);
        }
    }
}
