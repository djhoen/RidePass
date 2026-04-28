using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Redemption;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    [Authorize(Policy = TenantPermissions.Policy.SalesRedeem)]
    public class RedemptionController : ControllerBase
    {
        private readonly IDayPassPurchaseRepository _dayPasses;
        private readonly IEventTicketPurchaseRepository _tickets;
        private readonly ITenantContext _tenantContext;

        public RedemptionController(
            IDayPassPurchaseRepository dayPasses,
            IEventTicketPurchaseRepository tickets,
            ITenantContext tenantContext)
        {
            _dayPasses = dayPasses;
            _tickets = tickets;
            _tenantContext = tenantContext;
        }

        [HttpGet("Preview/{token:guid}")]
        public async Task<IActionResult> Preview(Guid token)
        {
            var preview = await LookupAsync(token);
            if (preview is null)
            {
                return new ApiResponses().NotFoundResult("No purchase found for this token in your tenant.");
            }
            return new ApiResponses().OkResult(preview);
        }

        [HttpPost("Redeem/{token:guid}")]
        public async Task<IActionResult> Redeem(Guid token)
        {
            var preview = await LookupAsync(token);
            if (preview is null)
            {
                return new ApiResponses().NotFoundResult("No purchase found for this token in your tenant.");
            }

            if (preview.Status == "redeemed")
            {
                return new ApiResponses().BadRequestResult("Already redeemed.");
            }
            if (preview.Status != "paid")
            {
                return new ApiResponses().BadRequestResult($"Cannot redeem a purchase with status '{preview.Status}'.");
            }

            if (!preview.IsRedeemableToday)
            {
                return new ApiResponses().BadRequestResult(preview.NotRedeemableReason ?? "This purchase is not redeemable today.");
            }

            if (preview.Kind == "day_pass")
            {
                await _dayPasses.UpdateStatus(preview.PurchaseId, "redeemed");
            }
            else
            {
                await _tickets.UpdateStatus(preview.PurchaseId, "redeemed");
            }

            preview.Status = "redeemed";
            return new ApiResponses().OkResult(preview);
        }

        private async Task<RedemptionPreviewResponse?> LookupAsync(Guid token)
        {
            var tenantId = _tenantContext.TenantId;
            var tz = ResolveTenantTimeZone();
            var todayInTenant = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).Date;

            var dp = await _dayPasses.GetByRedemptionToken(token, tenantId);
            if (dp is not null)
            {
                var (ok, reason) = CheckDayPassWindow(dp.ValidOnDate, todayInTenant);
                return new RedemptionPreviewResponse
                {
                    Kind = "day_pass",
                    PurchaseId = dp.Id,
                    RedemptionToken = dp.RedemptionToken,
                    PurchaserName = dp.PurchaserName,
                    PurchaserEmail = dp.PurchaserEmail,
                    ItemName = dp.ProductName,
                    AmountCents = dp.AmountCents,
                    Status = dp.Status,
                    ValidOnDate = dp.ValidOnDate,
                    CreatedAtUtc = DateTime.SpecifyKind(dp.CreatedAt, DateTimeKind.Utc),
                    IsRedeemableToday = ok,
                    NotRedeemableReason = reason,
                };
            }

            var tk = await _tickets.GetByRedemptionToken(token, tenantId);
            if (tk is not null)
            {
                var startUtc = DateTime.SpecifyKind(tk.EventStartsAt, DateTimeKind.Utc);
                var endUtc = DateTime.SpecifyKind(tk.EventEndsAt, DateTimeKind.Utc);
                var startInTenant = TimeZoneInfo.ConvertTimeFromUtc(startUtc, tz).Date;
                var endInTenant = TimeZoneInfo.ConvertTimeFromUtc(endUtc, tz).Date;

                var ok = todayInTenant >= startInTenant && todayInTenant <= endInTenant;
                string? reason = null;
                if (!ok)
                {
                    reason = todayInTenant < startInTenant
                        ? $"Event is on {startInTenant:yyyy-MM-dd} — too early to redeem."
                        : $"Event ended {endInTenant:yyyy-MM-dd} — ticket expired.";
                }

                return new RedemptionPreviewResponse
                {
                    Kind = "event_ticket",
                    PurchaseId = tk.Id,
                    RedemptionToken = tk.RedemptionToken,
                    PurchaserName = tk.PurchaserName,
                    PurchaserEmail = tk.PurchaserEmail,
                    ItemName = $"{tk.EventTitle} — {tk.TierName}",
                    AmountCents = tk.AmountCents,
                    Status = tk.Status,
                    EventTitle = tk.EventTitle,
                    TierName = tk.TierName,
                    EventDescription = tk.EventDescription,
                    EventLocationLabel = tk.EventLocationLabel,
                    EventStartsAtUtc = startUtc,
                    EventEndsAtUtc = endUtc,
                    EventAllDay = tk.EventAllDay,
                    CreatedAtUtc = DateTime.SpecifyKind(tk.CreatedAt, DateTimeKind.Utc),
                    IsRedeemableToday = ok,
                    NotRedeemableReason = reason,
                };
            }

            return null;
        }

        private static (bool ok, string? reason) CheckDayPassWindow(DateTime? validOnDate, DateTime todayInTenant)
        {
            if (!validOnDate.HasValue) return (true, null);
            var valid = validOnDate.Value.Date;
            if (todayInTenant == valid) return (true, null);
            return (false, todayInTenant < valid
                ? $"Pass is valid on {valid:yyyy-MM-dd} — too early to redeem."
                : $"Pass was valid on {valid:yyyy-MM-dd} — expired.");
        }

        private TimeZoneInfo ResolveTenantTimeZone()
        {
            var tz = _tenantContext.Tenant.Timezone;
            try { return TimeZoneInfo.FindSystemTimeZoneById(tz); }
            catch { return TimeZoneInfo.Utc; }
        }
    }
}
