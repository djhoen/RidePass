using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Redemption;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    // Wristband association at the gate: link a serialized band (QR payload or printed number) to
    // an admission, and resolve a scanned band back to its wearer. An admission is either an event
    // ticket or a season pass admission (a checked_in reservation). Tenant-controlled feature
    // (tenant.wristbands_enabled), off by default.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = TenantPermissions.Policy.SalesRedeem)]
    public class WristbandController : ControllerBase
    {
        private readonly IWristbandRepository _wristbands;
        private readonly IEventTicketPurchaseRepository _tickets;
        private readonly IEventTicketTierRepository _tiers;
        private readonly ISeasonPassRepository _passes;
        private readonly Services.Waivers.IWaiverCheckInGate _waiverGate;
        private readonly Services.Riders.IRiderIdVerification _idVerification;
        private readonly ITenantContext _tenantContext;

        public WristbandController(IWristbandRepository wristbands, IEventTicketPurchaseRepository tickets,
            IEventTicketTierRepository tiers, ISeasonPassRepository passes,
            Services.Waivers.IWaiverCheckInGate waiverGate,
            Services.Riders.IRiderIdVerification idVerification,
            ITenantContext tenantContext)
        {
            _wristbands = wristbands;
            _tickets = tickets;
            _tiers = tiers;
            _passes = passes;
            _waiverGate = waiverGate;
            _idVerification = idVerification;
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

        /// <summary>Today in the tenant's own timezone. Walk-up bands are scoped to a calendar day,
        /// so resolving one has to ask "today" the way the track does, not the way UTC does.</summary>
        private DateOnly TodayLocal()
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(_tenantContext.Tenant.Timezone);
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz));
        }

        // ── The band gate (tenant.require_id_for_wristband) ──────────────────────────────
        // A track that requires it will not put a band on anyone who has not both signed the
        // waiver and had their ID/age checked. Enforced HERE because Link is the single choke
        // point every band passes through, whichever credential anchors it. Gating only the
        // pass path would leave a ticket-anchored band as a way around it.
        //
        // The messages name the person and the missing item. "Not allowed" tells a gate worker
        // with a queue behind them nothing they can act on.

        private async Task<string?> BandBlockReasonForPass(SeasonPassReservationLinkContext reservation)
        {
            if (!_tenantContext.Tenant.RequireIdForWristband) return null;

            // The waiver is NOT re-checked here, deliberately. Admission already enforced it and
            // Link refuses anything but a checked_in reservation, so reaching this line is proof
            // it passed. Both admission paths (SeasonPassController.CheckIn and RedeemPassAtGate)
            // accept EITHER a signature on the pass itself OR the event's own rider waiver, so
            // testing season_pass_purchase.waiver_signature_id here would turn away a rider who
            // legitimately satisfied it the second way, with nothing they could do about it.
            //
            // GetPurchase is not tenant-scoped, so re-assert it. The id came from a tenant-scoped
            // reservation lookup, but a lookup that only happens to be safe is not the rule here.
            var pass = await _passes.GetPurchase(reservation.SeasonPassPurchaseId);
            if (pass is null || pass.TenantId != TenantId) return "That pass no longer exists.";

            var idStatus = await _idVerification.StatusForPass(pass, TenantId);
            if (!idStatus.Verified)
                return $"{reservation.HolderDisplayName}'s ID and age haven't been verified yet. "
                     + "Check their photo ID on the scan screen first, then link the band.";

            return null;
        }

        /// <summary>
        /// Ticket-anchored bands. Waiver only, deliberately.
        ///
        /// The storage and the resolution for a ticketed rider's ID both exist
        /// (event_ticket_purchase.id_verified_*, IRiderIdVerification.StatusForTicket), but the
        /// gate screen has no way to RECORD one against a ticket yet: that action is built for
        /// the season pass panel. Enforcing ID here would leave a track unable to band a ticketed
        /// rider at all, with nothing in the UI to fix it, which is worse than the gap it closes.
        /// Add the ID clause the moment a ticket-side verify action ships.
        /// </summary>
        private async Task<string?> BandBlockReasonForTicket(EventTicketPurchase ticket)
        {
            if (!_tenantContext.Tenant.RequireIdForWristband) return null;
            return await _waiverGate.BlockReasonForTicket(TenantId, ticket);
        }

        [HttpPost("Link")]
        public async Task<IActionResult> Link([FromBody] LinkWristbandRequest req)
        {
            if (Gate() is { } gated) return gated;
            var code = req.Code.Trim();
            if (code.Length == 0) return new ApiResponses().BadRequestResult("Scan or enter the band's code.");

            // Exactly one anchor. Checked here rather than by attribute: "one of two optional
            // fields" has no [Required] spelling.
            var hasTicket = req.TicketId.HasValue;
            var hasReservation = req.SeasonPassReservationId.HasValue;
            if (hasTicket == hasReservation)
                return new ApiResponses().BadRequestResult(
                    "Link a band to exactly one of a ticket or a season pass admission.");

            if (hasTicket)
            {
                // The ticket anchors the link; its tier resolves the event the code must be unique in.
                var ticket = await _tickets.GetById(req.TicketId!.Value, TenantId);
                if (ticket is null) return new ApiResponses().NotFoundResult("Ticket not found.");
                if (ticket.Status is not ("paid" or "redeemed"))
                    return new ApiResponses().BadRequestResult($"Only a paid entry can wear a band (this one is {ticket.Status}).");
                var tier = await _tiers.GetById(ticket.TierId, TenantId);
                if (tier is null) return new ApiResponses().BadRequestResult("This ticket's tier no longer exists.");

                if (await BandBlockReasonForTicket(ticket) is { } ticketBlock)
                    return new ApiResponses().BadRequestResult(ticketBlock);

                var conflictHolder = await _wristbands.Link(TenantId, tier.EventId, ticket.Id, code, UserId);
                if (conflictHolder is not null)
                    return new ApiResponses().BadRequestResult(
                        $"That band is already on {conflictHolder} for this event. Use a different band, or unlink theirs first.");
                return new ApiResponses().OkResult(new { code });
            }

            // Season pass admission. The reservation carries the scope the band inherits: its event
            // when one ran, otherwise the tenant-local date it was admitted on.
            var reservationId = req.SeasonPassReservationId!.Value;
            var reservation = await _passes.GetReservationForBandLink(reservationId, TenantId);
            if (reservation is null) return new ApiResponses().NotFoundResult("Season pass admission not found.");
            if (reservation.Status != "checked_in")
                return new ApiResponses().BadRequestResult(
                    "A band can only be linked after the pass has been admitted at the gate.");

            if (await BandBlockReasonForPass(reservation) is { } passBlock)
                return new ApiResponses().BadRequestResult(passBlock);

            var conflictHolderPass = await _wristbands.LinkToReservation(
                TenantId, reservationId, reservation.EventId, reservation.CheckInDate, code, UserId);
            if (conflictHolderPass is not null)
            {
                var scope = reservation.EventId is not null ? "this event" : "today";
                return new ApiResponses().BadRequestResult(
                    $"That band is already on {conflictHolderPass} for {scope}. Use a different band, or unlink theirs first.");
            }
            return new ApiResponses().OkResult(new { code });
        }

        [HttpPost("Unlink")]
        public async Task<IActionResult> Unlink([FromBody] UnlinkWristbandRequest req)
        {
            if (Gate() is { } gated) return gated;

            var hasTicket = req.TicketId.HasValue;
            var hasReservation = req.SeasonPassReservationId.HasValue;
            if (hasTicket == hasReservation)
                return new ApiResponses().BadRequestResult(
                    "Unlink a band from exactly one of a ticket or a season pass admission.");

            var n = hasTicket
                ? await _wristbands.UnlinkTicket(req.TicketId!.Value, TenantId)
                : await _wristbands.UnlinkReservation(req.SeasonPassReservationId!.Value, TenantId);
            return n == 0 ? new ApiResponses().NotFoundResult("No band is linked to that entry.") : new ApiResponses().OkResult();
        }

        // Scan a band → who is this? Returns the wearer + their redemption token so the gate UI
        // can jump straight to the order or the pass.
        [HttpGet("Resolve")]
        public async Task<IActionResult> Resolve([FromQuery] string code)
        {
            if (Gate() is { } gated) return gated;
            var trimmed = (code ?? "").Trim();
            if (trimmed.Length == 0) return new ApiResponses().BadRequestResult("Scan or enter the band's code.");
            var hit = await _wristbands.ResolveCode(TenantId, trimmed, TodayLocal());
            if (hit is null)
                return new ApiResponses().NotFoundResult("No entrant is linked to that band for a current event or today.");
            var riderName = $"{hit.RiderFirstName} {hit.RiderLastName}".Trim();
            return new ApiResponses().OkResult(new
            {
                hit.Source,
                hit.TicketId,
                hit.ReservationId,
                hit.PassPurchaseId,
                hit.EventId,
                hit.Code,
                RedemptionToken = hit.RedemptionToken,
                hit.EventTitle,
                hit.TierName,
                Status = hit.Status,
                Name = riderName.Length > 0 ? riderName : hit.PurchaserName,
                hit.RaceNumber,
                hit.LinkedAt,
            });
        }

        // Band codes for the entries on one gate view, so each row shows its band. Accepts either
        // or both id lists; the two maps come back separately because the id spaces are different.
        [HttpPost("Codes")]
        public async Task<IActionResult> Codes([FromBody] WristbandCodesRequest req)
        {
            if (Gate() is { } gated) return gated;
            if (req.TicketIds.Count == 0 && req.ReservationIds.Count == 0)
                return new ApiResponses().BadRequestResult("Ask for at least one ticket or season pass admission.");

            var ticketMap = req.TicketIds.Count > 0
                ? await _wristbands.GetCodesForTickets(req.TicketIds, TenantId)
                : new Dictionary<Guid, string>();
            var reservationMap = req.ReservationIds.Count > 0
                ? await _wristbands.GetCodesForReservations(req.ReservationIds, TenantId)
                : new Dictionary<Guid, string>();

            return new ApiResponses().OkResult(new
            {
                Tickets = ticketMap.Select(kv => new { ticketId = kv.Key, code = kv.Value }),
                Reservations = reservationMap.Select(kv => new { reservationId = kv.Key, code = kv.Value }),
            });
        }
    }
}
