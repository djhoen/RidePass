using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Dashboard;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class DashboardController : ControllerBase
    {
        private readonly IReportsRepository _reports;
        private readonly IEventRepository _events;
        private readonly IEventTicketPurchaseRepository _tickets;
        private readonly IDisputeRepository _disputes;
        private readonly IUserRepository _users;
        private readonly IRecentSalesRepository _recentSales;
        private readonly ITenantContext _tenantContext;

        public DashboardController(
            IReportsRepository reports,
            IEventRepository events,
            IEventTicketPurchaseRepository tickets,
            IDisputeRepository disputes,
            IUserRepository users,
            IRecentSalesRepository recentSales,
            ITenantContext tenantContext)
        {
            _reports = reports;
            _events = events;
            _tickets = tickets;
            _disputes = disputes;
            _users = users;
            _recentSales = recentSales;
            _tenantContext = tenantContext;
        }

        [HttpGet("Snapshot")]
        public async Task<IActionResult> GetSnapshot()
        {
            if (!_tenantContext.IsResolved)
            {
                return new ApiResponses().BadRequestResult("Dashboard is only available on a tenant subdomain.");
            }

            var role = User.FindFirst("role")?.Value ?? string.Empty;
            var perms = EffectivePermissionsFor(role);
            var tz = _tenantContext.Tenant.Timezone;
            var now = DateTime.UtcNow;

            var snapshot = new DashboardSnapshot
            {
                Permissions = perms.ToArray(),
                UpcomingEvents = (await _events.GetUpcomingWithType(_tenantContext.TenantId, 5))
                    .Select(e => new UpcomingEventDto
                    {
                        Id = e.Id,
                        Title = e.Title,
                        StartsAtUtc = DateTime.SpecifyKind(e.StartsAt, DateTimeKind.Utc),
                        EndsAtUtc = DateTime.SpecifyKind(e.EndsAt, DateTimeKind.Utc),
                        EventTypeName = e.EventTypeName,
                        EventTypeColor = e.EventTypeColor,
                        Capacity = e.Capacity,
                        LocationLabel = e.LocationLabel,
                    }).ToList(),
            };

            if (perms.Contains(TenantPermissions.ReportsView))
            {
                var todayStartLocal = TimeZoneInfo.ConvertTimeFromUtc(now, ResolveTz(tz)).Date;
                var todayStartUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(todayStartLocal, DateTimeKind.Unspecified), ResolveTz(tz));
                var tomorrowStartUtc = todayStartUtc.AddDays(1);
                var monthStartLocal = new DateTime(todayStartLocal.Year, todayStartLocal.Month, 1);
                var monthStartUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(monthStartLocal, DateTimeKind.Unspecified), ResolveTz(tz));
                var nextMonthStartUtc = monthStartUtc.AddMonths(1);
                var weekStartUtc = todayStartUtc.AddDays(-6);

                var todayTicket = await _reports.GetTicketTotals(_tenantContext.TenantId, todayStartUtc, tomorrowStartUtc);
                var monthTicket = await _reports.GetTicketTotals(_tenantContext.TenantId, monthStartUtc, nextMonthStartUtc);
                // All-kinds gross revenue (tickets + passes + memberships + extras + rentals +
                // concessions) so the headline matches the real money taken; the ticket count stays
                // event-ticket-specific.
                var todayByKind = await _reports.GetRevenueByKind(_tenantContext.TenantId, todayStartUtc, tomorrowStartUtc);
                var monthByKind = await _reports.GetRevenueByKind(_tenantContext.TenantId, monthStartUtc, nextMonthStartUtc);
                var riders = await _reports.GetUniqueRiders(_tenantContext.TenantId, monthStartUtc, nextMonthStartUtc);
                var daily = await _reports.GetDailyRevenue(_tenantContext.TenantId, weekStartUtc, tomorrowStartUtc, tz);

                snapshot.TodayRevenue = new RevenueBlockDto
                {
                    RevenueCents = todayByKind.Sum(r => r.RevenueCents),
                    PassesSold = 0,
                    TicketsSold = todayTicket.SoldCount,
                };
                snapshot.MonthRevenue = new RevenueBlockDto
                {
                    RevenueCents = monthByKind.Sum(r => r.RevenueCents),
                    PassesSold = 0,
                    TicketsSold = monthTicket.SoldCount,
                };
                snapshot.UniqueRidersMonth = riders;
                snapshot.Last7Days = daily
                    .Select(d => new DailySparkPointDto { Date = d.Date, RevenueCents = d.RevenueCents })
                    .ToList();
            }

            if (perms.Contains(TenantPermissions.SalesView))
            {
                // Reads from v_recent_sales (Script0080) so spectator passes,
                // event tickets, gift cards, rentals, etc. all show up — not
                // just day passes, which was the historical scope.
                var rows = await _recentSales.List(_tenantContext.TenantId,
                    now.AddDays(-30), now.AddDays(1), status: null, limit: 5);
                snapshot.RecentPurchases = rows
                    .Select(r => new RecentPurchaseDto
                    {
                        Id = r.Id,
                        Kind = r.Kind,
                        ProductName = r.ItemName ?? string.Empty,
                        PurchaserName = r.PurchaserName ?? string.Empty,
                        AmountCents = r.AmountCents,
                        Status = r.Status,
                        CreatedAtUtc = DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc),
                    }).ToList();
            }

            if (perms.Contains(TenantPermissions.DisputesView))
            {
                var disputes = await _disputes.ListByTenant(_tenantContext.TenantId);
                snapshot.OpenDisputesCount = disputes.Count(d =>
                    d.Status == "needs_response" || d.Status == "warning_needs_response");
            }

            if (perms.Contains(TenantPermissions.SalesCancel))
            {
                // Cancelled but not yet refunded — tenant admin action queue. Tenant-scoped count
                // (don't pull every tenant's cancelled rows just to filter in memory).
                snapshot.PendingRefundsCount = await _tickets.CountByStatusForTenant(_tenantContext.TenantId, "cancelled");
            }

            return new ApiResponses().OkResult(snapshot);
        }

        [HttpGet("Config")]
        public async Task<IActionResult> GetConfig()
        {
            if (!TryGetUserId(out var userId))
            {
                return new ApiResponses().BadRequestResult("Invalid token.");
            }
            var json = await _users.GetDashboardConfig(userId);
            return new ApiResponses().OkResult(new { config = json });
        }

        [HttpPut("Config")]
        public async Task<IActionResult> SetConfig([FromBody] DashboardConfigDto body)
        {
            if (!TryGetUserId(out var userId))
            {
                return new ApiResponses().BadRequestResult("Invalid token.");
            }
            // Store the caller's JSON verbatim — it's per-user, not query-able, and new widget
            // types should not require a server change.
            var json = System.Text.Json.JsonSerializer.Serialize(body);
            await _users.SetDashboardConfig(userId, json);
            return new ApiResponses().OkResult(new { saved = true });
        }

        private bool TryGetUserId(out Guid userId)
        {
            var claim = User.FindFirst("UserId")?.Value;
            return Guid.TryParse(claim, out userId);
        }

        private static HashSet<string> EffectivePermissionsFor(string role) =>
            role == "super_admin"
                ? new HashSet<string>(TenantPermissions.All)
                : new HashSet<string>(TenantPermissions.ForRole(role));

        private static TimeZoneInfo ResolveTz(string iana)
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(iana); }
            catch { return TimeZoneInfo.Utc; }
        }
    }
}
