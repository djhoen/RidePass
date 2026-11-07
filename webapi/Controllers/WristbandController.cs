using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Redemption;
using webapi.Helpers;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    // Wristband association at the gate: link a serialized band (QR payload or printed number) to
    // an admission, and resolve a scanned band back to its wearer. Tenant-controlled feature
    // (tenant.wristbands_enabled), off by default.
    //
    // Two stores (Script0312):
    //   * rider_day_band: a band on a RIDER for the tenant's business day, covering every admission they
    //     have that day. Season pass holders are banded here (IRiderDayBands holds every rule).
    //   * event_wristband (Script0189 / Script0237): a band on one ticket (unchanged until item 10 moves
    //     tickets onto day bands), and older pass rows, which keep resolving.
    // A code is never allowed to mean two riders on the same day across the two.
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
        private readonly IRiderDayBands _dayBands;
        private readonly ITenantContext _tenantContext;

        public WristbandController(IWristbandRepository wristbands, IEventTicketPurchaseRepository tickets,
            IEventTicketTierRepository tiers, ISeasonPassRepository passes,
            Services.Waivers.IWaiverCheckInGate waiverGate,
            IRiderDayBands dayBands,
            ITenantContext tenantContext)
        {
            _wristbands = wristbands;
            _tickets = tickets;
            _tiers = tiers;
            _passes = passes;
            _waiverGate = waiverGate;
            _dayBands = dayBands;
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
        private DateOnly TodayLocal() => _dayBands.Today();

        /// <summary>
        /// Ticket-anchored bands. Waiver only, deliberately (tenant.require_id_for_wristband).
        ///
        /// The storage and the resolution for a ticketed rider's ID both exist
        /// (event_ticket_purchase.id_verified_*, IRiderIdVerification.StatusForTicket), but the
        /// gate screen has no way to RECORD one against a ticket yet: that action is built for
        /// the season pass panel. Enforcing ID here would leave a track unable to band a ticketed
        /// rider at all, with nothing in the UI to fix it, which is worse than the gap it closes.
        /// Add the ID clause the moment a ticket-side verify action ships.
        /// </summary>
        private async Task<Services.Waivers.WaiverGateDecision?> BandBlockForTicket(EventTicketPurchase ticket)
        {
            if (!_tenantContext.Tenant.RequireIdForWristband) return null;
            var decision = await _waiverGate.EvaluateTicket(TenantId, ticket);
            return decision.BlockReason is null ? null : decision;
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

                if (await BandBlockForTicket(ticket) is { } ticketBlock)
                    return GateBlockResult.Result(ticketBlock, "ticket", ticket.RedemptionToken, ticket.Id);

                // A code on a rider's day band today means that rider (Script0312). (Another ticket's band at this
                // event is refused by the event-scoped link below, as before.)
                if (await _dayBands.CodeInUse(code, TodayLocal()) is { DayBand: { } dayBand })
                    return new ApiResponses().BadRequestResult(
                        $"That band is already on {dayBand.RiderName} today. Use a different band.");

                var conflictHolder = await _wristbands.Link(TenantId, tier.EventId, ticket.Id, code, UserId);
                if (conflictHolder is not null)
                    return new ApiResponses().BadRequestResult(
                        $"That band is already on {conflictHolder} for this event. Use a different band, or unlink theirs first.");
                return new ApiResponses().OkResult(new { code });
            }

            // Season pass admission: the holder's band for the day (Script0312), through the same rules as
            // "Admit and band" (gate clear, ID when required, one band per rider and one rider per band a day).
            var reservationId = req.SeasonPassReservationId!.Value;
            var reservation = await _passes.GetReservationForBandLink(reservationId, TenantId);
            if (reservation is null) return new ApiResponses().NotFoundResult("Season pass admission not found.");
            if (reservation.Status != "checked_in")
                return new ApiResponses().BadRequestResult(
                    "A band can only be linked after the pass has been admitted at the gate.");
            // GetPurchase is not tenant-scoped, so re-assert it.
            var pass = await _passes.GetPurchase(reservation.SeasonPassPurchaseId);
            if (pass is null || pass.TenantId != TenantId) return new ApiResponses().NotFoundResult("That pass no longer exists.");
            var product = await _passes.GetProduct(pass.ProductId, TenantId);
            // The band is for the admission's own business day (its walk-up date, else its event's local day),
            // judged against its own event. Linking a different band to an admission that already carries the
            // rider's band corrects it (a replacement "corrected at the gate", audited).
            var result = await _dayBands.IssueForPass(pass, product, reservationId, reservation.EventId, code, null, UserId,
                businessDate: reservation.BusinessDate ?? TodayLocal(), correcting: reservationId);
            if (!result.Ok)
                return result.GateBlock is { } block
                    ? GateBlockResult.Result(block, "season_pass", pass.RedemptionToken, pass.Id, reservation.EventId)
                    : new ApiResponses().BadRequestResult(result.Error!);
            return new ApiResponses().OkResult(new { code = result.Band!.Code });
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
                // Only this admission comes off the rider's day band; the band stays worn (it may cover their other
                // admissions, and it is still on their wrist). Any older row for the admission is removed.
                : await _dayBands.UnlinkAdmission(req.SeasonPassReservationId!.Value)
                  + await _wristbands.UnlinkReservation(req.SeasonPassReservationId!.Value, TenantId);
            return n == 0 ? new ApiResponses().NotFoundResult("No band is linked to that entry.") : new ApiResponses().OkResult();
        }

        // Scan a band → who is this? Returns the wearer + their redemption token so the gate UI
        // can jump straight to the order or the pass. A rider's day band (today only) first.
        [HttpGet("Resolve")]
        public async Task<IActionResult> Resolve([FromQuery] string code)
        {
            if (Gate() is { } gated) return gated;
            var trimmed = (code ?? "").Trim();
            if (trimmed.Length == 0) return new ApiResponses().BadRequestResult("Scan or enter the band's code.");

            // One question for both stores, the same one a band issue asks: who has this code today?
            var use = await _dayBands.CodeInUse(trimmed, TodayLocal());
            if (use?.DayBand is { } day)
            {
                var admissions = await _dayBands.Admissions(day.Id);
                var pass = day.SeasonPassPurchaseId is Guid pid ? await _passes.GetPurchase(pid) : null;
                if (pass is not null && pass.TenantId != TenantId) pass = null;
                var latest = admissions.OrderByDescending(a => a.CheckedInAt ?? a.LinkedAt).FirstOrDefault();
                // The rider's photo on file, as the gate shows it (the pass card loads it too).
                var photo = pass is not null ? await _waiverGate.EvaluatePass(TenantId, pass.Id, latest?.EventId) : null;
                return new ApiResponses().OkResult(new
                {
                    Source = "rider_day",
                    BandId = day.Id,
                    TicketId = (Guid?)null,
                    ReservationId = latest?.SeasonPassReservationId,
                    PassPurchaseId = day.SeasonPassPurchaseId,
                    EventId = latest?.EventId,
                    day.Code,
                    RedemptionToken = pass?.RedemptionToken,
                    EventTitle = latest?.EventTitle ?? "Day pass",
                    TierName = (string?)null,
                    Status = latest?.Status ?? "checked_in",
                    Name = day.RiderName,
                    RaceNumber = (string?)null,
                    LinkedAt = DateTime.SpecifyKind(day.IssuedAt, DateTimeKind.Utc),
                    day.BusinessDate,
                    IssuedAtLocal = _dayBands.IssuedTime(day),
                    Admissions = admissions.Select(a => new
                    {
                        a.SeasonPassReservationId,
                        a.EventTicketPurchaseId,
                        a.EventId,
                        EventTitle = a.EventTitle ?? "Walk-up admission",
                        a.Status,
                        CheckedInAtUtc = a.CheckedInAt is { } ci ? DateTime.SpecifyKind(ci, DateTimeKind.Utc) : (DateTime?)null,
                    }),
                    HasPhoto = photo?.HasPhoto ?? false,
                    PhotoSource = photo?.PhotoSource,
                    RiderPhotoId = photo?.RiderPhotoId,
                    PhotoUrl = photo?.PhotoUrl,
                });
            }

            var hit = use?.Legacy;
            if (hit is null)
                return new ApiResponses().NotFoundResult("No rider wears that band today.");
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
            // A day band covering the admission wins over an older row.
            if (req.ReservationIds.Count > 0)
                foreach (var kv in await _dayBands.CodesForReservations(req.ReservationIds))
                    reservationMap[kv.Key] = kv.Value;

            return new ApiResponses().OkResult(new
            {
                Tickets = ticketMap.Select(kv => new { ticketId = kv.Key, code = kv.Value }),
                Reservations = reservationMap.Select(kv => new { reservationId = kv.Key, code = kv.Value }),
            });
        }
    }
}
