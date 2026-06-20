using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.LoamPassMx;
using Services.Repositories.Data.UserData;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.User;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    /// <summary>
    /// Lets an authenticated rider link their LoamMx (LoamPassMx) account to their RidePass
    /// account (email + 6-digit code) and see their redeemable credit balance at the current
    /// track. The actual redemption happens at checkout (see PurchaseController).
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class RiderLoampassController : ControllerBase
    {
        private readonly IRiderLoampassLinkRepository _links;
        private readonly ILoamPassMxService _loampass;
        private readonly ITenantContext _tenantContext;
        private readonly IEventTicketPurchaseRepository _ticketPurchases;
        private readonly Services.Waivers.IWaiverCheckInGate _waiverGate;

        public RiderLoampassController(
            IRiderLoampassLinkRepository links,
            ILoamPassMxService loampass,
            ITenantContext tenantContext,
            IEventTicketPurchaseRepository ticketPurchases,
            Services.Waivers.IWaiverCheckInGate waiverGate)
        {
            _links = links;
            _loampass = loampass;
            _tenantContext = tenantContext;
            _ticketPurchases = ticketPurchases;
            _waiverGate = waiverGate;
        }

        [HttpGet("Status")]
        public async Task<IActionResult> Status()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!TryGetUserId(out var userId)) return new ApiResponses().BadRequestResult("Invalid token.");

            var destinationId = _tenantContext.Tenant.LoampassMxDestinationId;
            var trackParticipates = !string.IsNullOrWhiteSpace(destinationId);
            var links = await _links.ListByUserId(userId, _tenantContext.TenantId);

            int? creditsAvailable = null;
            if (trackParticipates && links.Count > 0)
            {
                // Aggregate across every linked Loam account for this destination.
                var total = 0;
                foreach (var l in links)
                {
                    total += await _loampass.GetCreditsAsync(l.LoampassAccountId, destinationId!);
                }
                creditsAvailable = total;
            }

            return new ApiResponses().OkResult(new
            {
                trackParticipates,
                linked = links.Count > 0,
                accounts = links.Select(l => new { loampassEmail = l.LoampassEmail, loampassAccountId = l.LoampassAccountId }).ToList(),
                creditsAvailable,
            });
        }

        [HttpPost("LinkStart")]
        public async Task<IActionResult> LinkStart([FromBody] LoampassLinkStartRequest request)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!TryGetUserId(out _)) return new ApiResponses().BadRequestResult("Invalid token.");
            if (!_loampass.IsConfigured) return new ApiResponses().BadRequestResult("Loam Pass linking isn't available right now.");
            if (string.IsNullOrWhiteSpace(request?.Email)) return new ApiResponses().BadRequestResult("Email is required.");

            await _loampass.VerifyStartAsync(request.Email.Trim());
            // Neutral response (LoamMx also doesn't reveal whether the email exists).
            return new ApiResponses().OkResult(new { sent = true });
        }

        [HttpPost("LinkConfirm")]
        public async Task<IActionResult> LinkConfirm([FromBody] LoampassLinkConfirmRequest request)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!TryGetUserId(out var userId)) return new ApiResponses().BadRequestResult("Invalid token.");
            if (string.IsNullOrWhiteSpace(request?.Email) || string.IsNullOrWhiteSpace(request?.Code))
                return new ApiResponses().BadRequestResult("Email and code are required.");

            var account = await _loampass.VerifyConfirmAsync(request.Email.Trim(), request.Code.Trim());
            if (account is null) return new ApiResponses().BadRequestResult("That code is invalid or expired.");

            await _links.Add(new RiderLoampassLink
            {
                UserId = userId,
                TenantId = _tenantContext.TenantId,
                LoampassAccountId = account.AccountId,
                LoampassEmail = account.Email,
            });

            return new ApiResponses().OkResult(new { linked = true, loampassEmail = account.Email });
        }

        [HttpDelete]
        public async Task<IActionResult> Unlink([FromQuery] string accountId)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!TryGetUserId(out var userId)) return new ApiResponses().BadRequestResult("Invalid token.");
            if (string.IsNullOrWhiteSpace(accountId)) return new ApiResponses().BadRequestResult("accountId is required.");
            await _links.DeleteByAccount(userId, _tenantContext.TenantId, accountId.Trim());
            return new ApiResponses().OkResult();
        }

        // Staff scan a rider's Loam Pass QR at the gate to check in their EXISTING reservation for
        // the selected event. The credit was spent at booking, so this never spends another — it
        // just marks the rider's race entry redeemed so the track sees who showed up (esp. for races).
        [Authorize(Policy = TenantPermissions.Policy.SalesRedeem)]
        [HttpPost("GateCheckIn")]
        public async Task<IActionResult> GateCheckIn([FromBody] LoampassGateCheckInRequest request)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!TryGetUserId(out var staffId)) return new ApiResponses().BadRequestResult("Invalid token.");
            if (request is null || string.IsNullOrWhiteSpace(request.PassQr) || request.EventId == Guid.Empty)
                return new ApiResponses().BadRequestResult("passQr and eventId are required.");

            var owner = await _loampass.GetPassOwnerAsync(ParsePassId(request.PassQr));
            if (owner is null) return new ApiResponses().BadRequestResult("That Loam Pass wasn't recognized.");

            var userId = await _links.GetUserIdByAccount(owner.AccountId, _tenantContext.TenantId);
            if (userId is null)
                return new ApiResponses().BadRequestResult(
                    "This Loam Pass isn't linked to a rider here. The rider can connect it in their profile and book online.");

            var tickets = await _ticketPurchases.GetForUser(userId.Value, _tenantContext.TenantId);
            var entry = tickets.FirstOrDefault(t => t.EventId == request.EventId && t.TierKind == "race_entry" && t.Status == "paid");
            if (entry is null)
            {
                var already = tickets.Any(t => t.EventId == request.EventId && t.TierKind == "race_entry" && t.Status == "redeemed");
                return new ApiResponses().BadRequestResult(
                    already ? "This rider is already checked in for this event."
                            : "No reservation found for this rider at this event.");
            }

            // A required event waiver can't be skipped at the Loam Pass gate either.
            var ticketRow = await _ticketPurchases.GetById(entry.Id, _tenantContext.TenantId);
            if (ticketRow is not null)
            {
                var waiverBlock = await _waiverGate.BlockReasonForTicket(_tenantContext.TenantId, ticketRow);
                if (waiverBlock is not null) return new ApiResponses().BadRequestResult(waiverBlock);
            }

            await _ticketPurchases.MarkRedeemed(entry.Id, _tenantContext.TenantId, staffId, DateTime.UtcNow);
            return new ApiResponses().OkResult(new { checkedIn = true, riderName = entry.PurchaserName, item = entry.TierName });
        }

        // A Loam Pass QR is "{issuer}/QR/{passId}"; accept the full URL or a bare pass id.
        private static string ParsePassId(string qr)
        {
            var s = qr.Trim();
            var slash = s.LastIndexOf('/');
            return slash >= 0 ? s[(slash + 1)..] : s;
        }

        private bool TryGetUserId(out Guid userId)
        {
            var claim = User.FindFirst("UserId")?.Value;
            return Guid.TryParse(claim, out userId);
        }
    }
}
