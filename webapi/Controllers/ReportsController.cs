using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Data.ReportData;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Reports;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ReportsController : ControllerBase
    {
        private readonly IReportsRepository _reports;
        private readonly ITenantContext _tenantContext;

        public ReportsController(IReportsRepository reports, ITenantContext tenantContext)
        {
            _reports = reports;
            _tenantContext = tenantContext;
        }

        [Authorize(Policy = TenantPermissions.Policy.ReportsView)]
        [HttpGet("Admin/Summary")]
        public async Task<IActionResult> GetTenantSummary([FromQuery] DateTime fromUtc, [FromQuery] DateTime toUtc)
        {
            if (toUtc <= fromUtc)
            {
                return new ApiResponses().BadRequestResult("toUtc must be after fromUtc.");
            }

            var tenantId = _tenantContext.TenantId;
            var tz = _tenantContext.Tenant.Timezone;

            var dayPass = await _reports.GetDayPassTotals(tenantId, fromUtc, toUtc);
            var ticket = await _reports.GetTicketTotals(tenantId, fromUtc, toUtc);
            var riders = await _reports.GetUniqueRiders(tenantId, fromUtc, toUtc);
            var disputes = await _reports.GetDisputeCount(tenantId, fromUtc, toUtc);
            var daily = await _reports.GetDailyRevenue(tenantId, fromUtc, toUtc, tz);
            var topProducts = await _reports.GetTopDayPassProducts(tenantId, fromUtc, toUtc);
            var topEvents = await _reports.GetTopEvents(tenantId, fromUtc, toUtc);

            var summary = new TenantReportSummary
            {
                FromUtc = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc),
                ToUtc = DateTime.SpecifyKind(toUtc, DateTimeKind.Utc),
                TotalRevenueCents = dayPass.RevenueCents + ticket.RevenueCents,
                PassesSold = dayPass.SoldCount,
                TicketsSold = ticket.SoldCount,
                UniqueRiders = riders,
                RefundedCount = dayPass.RefundedCount + ticket.RefundedCount,
                CancelledCount = dayPass.CancelledCount + ticket.CancelledCount,
                DisputedCount = disputes,
                RefundedAmountCents = dayPass.RefundedCents + ticket.RefundedCents,
                DailyRevenue = daily.Select(MapDaily).ToList(),
                TopDayPassProducts = topProducts.Select(p => new TopProductDto
                {
                    ProductId = p.ProductId,
                    ProductName = p.ProductName,
                    SoldCount = p.SoldCount,
                    RevenueCents = p.RevenueCents,
                }).ToList(),
                TopEvents = topEvents.Select(e => new TopEventDto
                {
                    EventId = e.EventId,
                    EventTitle = e.EventTitle,
                    EventStartUtc = DateTime.SpecifyKind(e.EventStartUtc, DateTimeKind.Utc),
                    SoldCount = e.SoldCount,
                    RevenueCents = e.RevenueCents,
                }).ToList(),
            };

            return new ApiResponses().OkResult(summary);
        }

        private static DailyRevenuePointDto MapDaily(DailyRevenuePoint p) => new()
        {
            Date = p.Date,
            RevenueCents = p.RevenueCents,
            PassesSold = p.PassesSold,
            TicketsSold = p.TicketsSold,
        };
    }
}
