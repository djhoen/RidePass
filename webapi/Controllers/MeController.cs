using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Interfaces;
using webapi.Controllers.API.Data.Me;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    [Authorize]
    public class MeController : ControllerBase
    {
        private readonly IDayPassPurchaseRepository _dayPasses;
        private readonly IEventTicketPurchaseRepository _tickets;
        private readonly ITenantContext _tenantContext;

        public MeController(
            IDayPassPurchaseRepository dayPasses,
            IEventTicketPurchaseRepository tickets,
            ITenantContext tenantContext)
        {
            _dayPasses = dayPasses;
            _tickets = tickets;
            _tenantContext = tenantContext;
        }

        [HttpGet("Purchases")]
        public async Task<IActionResult> GetMyPurchases()
        {
            if (!_tenantContext.IsResolved)
            {
                return new ApiResponses().BadRequestResult("No tenant resolved.");
            }

            var userIdClaim = User.FindFirst("UserId")?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
            {
                return new ApiResponses().BadRequestResult("Invalid token.");
            }

            var dayPasses = await _dayPasses.GetForUser(userId, _tenantContext.TenantId);
            var tickets = await _tickets.GetForUser(userId, _tenantContext.TenantId);

            var combined = new List<MyPurchaseResponse>();
            combined.AddRange(dayPasses.Select(dp => new MyPurchaseResponse
            {
                Kind = "day_pass",
                Id = dp.Id,
                ItemName = dp.ProductName,
                ValidOnDate = dp.ValidOnDate,
                AmountCents = dp.AmountCents,
                Status = dp.Status,
                RedemptionToken = dp.RedemptionToken,
                CreatedAtUtc = DateTime.SpecifyKind(dp.CreatedAt, DateTimeKind.Utc),
            }));
            combined.AddRange(tickets.Select(tk => new MyPurchaseResponse
            {
                Kind = "event_ticket",
                Id = tk.Id,
                ItemName = $"{tk.EventTitle} — {tk.TierName}",
                EventId = tk.EventId,
                EventStartsAtUtc = DateTime.SpecifyKind(tk.EventStartsAt, DateTimeKind.Utc),
                AmountCents = tk.AmountCents,
                Status = tk.Status,
                RedemptionToken = tk.RedemptionToken,
                CreatedAtUtc = DateTime.SpecifyKind(tk.CreatedAt, DateTimeKind.Utc),
            }));

            var ordered = combined.OrderByDescending(p => p.CreatedAtUtc).ToList();
            return new ApiResponses().OkResult(ordered);
        }
    }
}
