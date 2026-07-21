using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Redemption;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    // Wristband association at the gate: link a serialized band (QR payload or printed number) to
    // an event entrant, and resolve a scanned band back to its entrant. Tenant-controlled feature
    // (tenant.wristbands_enabled), off by default.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = TenantPermissions.Policy.SalesRedeem)]
    public class WristbandController : ControllerBase
    {
        private readonly IWristbandRepository _wristbands;
        private readonly IEventTicketPurchaseRepository _tickets;
        private readonly IEventTicketTierRepository _tiers;
        private readonly ITenantContext _tenantContext;

        public WristbandController(IWristbandRepository wristbands, IEventTicketPurchaseRepository tickets,
            IEventTicketTierRepository tiers, ITenantContext tenantContext)
        {
            _wristbands = wristbands;
            _tickets = tickets;
            _tiers = tiers;
            _tenantContext = tenantContext;
        }

        private Guid TenantId => _tenantContext.TenantId;
        private Guid? UserId => Guid.TryParse(User.FindFirst("UserId")?.Value, out var id) ? id : null;

        private IActionResult? Gate()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!_tenantContext.Tenant.WristbandsEnabled)
                return new ApiResponses().BadRequestResult("Wristbands aren't turned on for this track (Settings → Features).");
            return null;
        }

        [HttpPost("Link")]
        public async Task<IActionResult> Link([FromBody] LinkWristbandRequest req)
        {
            if (Gate() is { } gated) return gated;
            var code = req.Code.Trim();
            if (code.Length == 0) return new ApiResponses().BadRequestResult("Scan or enter the band's code.");

            // The ticket anchors the link; its tier resolves the event the code must be unique in.
            var ticket = await _tickets.GetById(req.TicketId, TenantId);
            if (ticket is null) return new ApiResponses().NotFoundResult("Ticket not found.");
            if (ticket.Status is not ("paid" or "redeemed"))
                return new ApiResponses().BadRequestResult($"Only a paid entry can wear a band (this one is {ticket.Status}).");
            var tier = await _tiers.GetById(ticket.TierId, TenantId);
            if (tier is null) return new ApiResponses().BadRequestResult("This ticket's tier no longer exists.");

            var conflictHolder = await _wristbands.Link(TenantId, tier.EventId, ticket.Id, code, UserId);
            if (conflictHolder is not null)
                return new ApiResponses().BadRequestResult(
                    $"That band is already on {conflictHolder} for this event. Use a different band, or unlink theirs first.");
            return new ApiResponses().OkResult(new { code });
        }

        [HttpPost("Unlink")]
        public async Task<IActionResult> Unlink([FromBody] UnlinkWristbandRequest req)
        {
            if (Gate() is { } gated) return gated;
            var n = await _wristbands.UnlinkTicket(req.TicketId, TenantId);
            return n == 0 ? new ApiResponses().NotFoundResult("No band is linked to that entry.") : new ApiResponses().OkResult();
        }

        // Scan a band → who is this? Returns the entrant + their redemption token so the gate UI
        // can jump straight to the order.
        [HttpGet("Resolve")]
        public async Task<IActionResult> Resolve([FromQuery] string code)
        {
            if (Gate() is { } gated) return gated;
            var trimmed = (code ?? "").Trim();
            if (trimmed.Length == 0) return new ApiResponses().BadRequestResult("Scan or enter the band's code.");
            var hit = await _wristbands.ResolveCode(TenantId, trimmed);
            if (hit is null)
                return new ApiResponses().NotFoundResult("No entrant is linked to that band for a current event.");
            var riderName = $"{hit.RiderFirstName} {hit.RiderLastName}".Trim();
            return new ApiResponses().OkResult(new
            {
                hit.TicketId,
                hit.EventId,
                hit.Code,
                RedemptionToken = hit.RedemptionToken,
                hit.EventTitle,
                hit.TierName,
                Status = hit.TicketStatus,
                Name = riderName.Length > 0 ? riderName : hit.PurchaserName,
                hit.RaceNumber,
                hit.LinkedAt,
            });
        }

        // Band codes for the tickets on one gate order view, so each row shows its band.
        [HttpPost("Codes")]
        public async Task<IActionResult> Codes([FromBody] WristbandCodesRequest req)
        {
            if (Gate() is { } gated) return gated;
            var map = await _wristbands.GetCodesForTickets(req.TicketIds, TenantId);
            return new ApiResponses().OkResult(map.Select(kv => new { ticketId = kv.Key, code = kv.Value }));
        }
    }
}
