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
        private readonly IEventRepository _events;
        private readonly IWaiverRepository _waivers;
        private readonly IMembershipRepository _memberships;
        private readonly IEventTicketPurchaseRepository _tickets;
        private readonly ISeasonPassRepository _seasonPasses;
        private readonly ISmsSender _sms;
        private readonly ISmtpEmailer _emailer;
        private readonly IScheduledTaskRepository _scheduledTasks;
        private readonly Services.Waivers.IWaiverCheckInGate _waiverGate;
        private readonly ITenantContext _tenantContext;

        public ReportsController(
            IReportsRepository reports,
            IEventRepository events,
            IWaiverRepository waivers,
            IMembershipRepository memberships,
            IEventTicketPurchaseRepository tickets,
            ISeasonPassRepository seasonPasses,
            ISmsSender sms,
            ISmtpEmailer emailer,
            IScheduledTaskRepository scheduledTasks,
            Services.Waivers.IWaiverCheckInGate waiverGate,
            ITenantContext tenantContext)
        {
            _reports = reports;
            _events = events;
            _waivers = waivers;
            _memberships = memberships;
            _tickets = tickets;
            _seasonPasses = seasonPasses;
            _sms = sms;
            _emailer = emailer;
            _scheduledTasks = scheduledTasks;
            _waiverGate = waiverGate;
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

            var ticket = await _reports.GetTicketTotals(tenantId, fromUtc, toUtc);
            var revenueByKind = await _reports.GetRevenueByKind(tenantId, fromUtc, toUtc);
            var riders = await _reports.GetUniqueRiders(tenantId, fromUtc, toUtc);
            var disputes = await _reports.GetDisputeCount(tenantId, fromUtc, toUtc);
            var daily = await _reports.GetDailyRevenue(tenantId, fromUtc, toUtc, tz);
            var topEvents = await _reports.GetTopEvents(tenantId, fromUtc, toUtc);

            var summary = new TenantReportSummary
            {
                FromUtc = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc),
                ToUtc = DateTime.SpecifyKind(toUtc, DateTimeKind.Utc),
                // All-kinds gross revenue from the ledger (tickets + passes + memberships + extras +
                // rentals + concessions), broken out by type. TicketsSold stays the event-ticket count.
                TotalRevenueCents = revenueByKind.Sum(r => r.RevenueCents),
                PassesSold = 0,
                TicketsSold = ticket.SoldCount,
                UniqueRiders = riders,
                RefundedCount = ticket.RefundedCount,
                CancelledCount = ticket.CancelledCount,
                DisputedCount = disputes,
                RefundedAmountCents = ticket.RefundedCents,
                RevenueByType = revenueByKind.Select(r => new RevenueByKindDto
                {
                    Kind = r.SourceKind,
                    RevenueCents = r.RevenueCents,
                    SaleCount = r.SaleCount,
                }).ToList(),
                DailyRevenue = daily.Select(MapDaily).ToList(),
                TopPassProducts = new List<TopProductDto>(),
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

        // ── Event Riders ────────────────────────────────────────────────────
        // Roll-call for one event: every paid registrant across pass / ticket /
        // season-pass-reservation, with their check-in status. Used for the gate
        // staff handout and the post-event "who actually showed?" view.
        [Authorize(Policy = TenantPermissions.Policy.ReportsView)]
        [HttpGet("Admin/EventRiders/{eventId:guid}")]
        public async Task<IActionResult> GetEventRiders(Guid eventId)
        {
            var ev = await _events.GetById(eventId, _tenantContext.TenantId);
            if (ev is null) return new ApiResponses().NotFoundResult("Event not found.");

            var rows = await _reports.GetEventRiders(_tenantContext.TenantId, eventId);
            var resp = new EventRiderReportResponse
            {
                EventId = ev.Id,
                EventTitle = ev.Title,
                EventStartsAtUtc = DateTime.SpecifyKind(ev.StartsAt, DateTimeKind.Utc),
                TotalRegistrants = rows.Sum(r => r.Quantity),
                TotalCheckedIn = rows.Where(r => r.CheckedIn).Sum(r => r.Quantity),
                Rows = rows.Select(r =>
                {
                    var (first, last) = SplitName(r.FirstName, r.LastName, r.PurchaserName);
                    return new EventRiderRowDto
                    {
                        PurchaseId = r.PurchaseId,
                        Source = r.Source,
                        PurchaserName = r.PurchaserName,
                        FirstName = first,
                        LastName = last,
                        PurchaserEmail = r.PurchaserEmail,
                        PurchaserPhone = r.PurchaserPhone,
                        ItemName = r.ItemName,
                        TierKind = r.TierKind,
                        TierAudience = r.TierAudience,
                        RaceNumber = r.RaceNumber,
                        UserRaceNumber = r.UserRaceNumber,
                        Hometown = r.Hometown,
                        Quantity = r.Quantity,
                        AmountCents = r.AmountCents,
                        Status = r.Status,
                        CheckedIn = r.CheckedIn,
                        CheckedInAtUtc = r.CheckedInAtUtc.HasValue
                            ? DateTime.SpecifyKind(r.CheckedInAtUtc.Value, DateTimeKind.Utc) : null,
                        CreatedAtUtc = DateTime.SpecifyKind(r.CreatedAtUtc, DateTimeKind.Utc),
                    };
                }).ToList(),
            };
            return new ApiResponses().OkResult(resp);
        }

        // ── Per-row actions for the Event Riders report ─────────────────────
        // SalesRedeem permission so any staff member running the gate / pit
        // tent can flip these fields. (ReportsView is read-only.)
        [Authorize(Policy = TenantPermissions.Policy.SalesRedeem)]
        [HttpPut("Admin/EventRiders/{purchaseId:guid}/CheckIn")]
        public async Task<IActionResult> SetCheckIn(Guid purchaseId, [FromBody] SetCheckInRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!Guid.TryParse(User.FindFirst("UserId")?.Value, out var staffId))
                return new ApiResponses().BadRequestResult("Invalid token.");

            var tenantId = _tenantContext.TenantId;
            switch (req.Source)
            {
                case "event_ticket":
                    if (req.CheckedIn)
                    {
                        // A required event waiver can't be skipped, even on the admin check-in toggle.
                        var t = await _tickets.GetById(purchaseId, tenantId);
                        if (t is not null)
                        {
                            var waiverBlock = await _waiverGate.BlockReasonForTicket(tenantId, t);
                            if (waiverBlock is not null) return new ApiResponses().BadRequestResult(waiverBlock);
                        }
                        await _tickets.MarkRedeemed(purchaseId, tenantId, staffId, DateTime.UtcNow);
                    }
                    else await _tickets.UndoRedeemed(purchaseId, tenantId);
                    break;
                case "season_pass":
                    if (req.CheckedIn)
                    {
                        // A season-pass holder is a rider; enforce the event's rider waiver at check-in.
                        var ctx = await _seasonPasses.GetReservationForCheckIn(purchaseId, tenantId);
                        if (ctx is not null)
                        {
                            var waiverBlock = await _waiverGate.BlockReason(tenantId, ctx.EventId,
                                riderAudience: true, ctx.HolderUserId, ctx.HolderEmail, ctx.HolderName);
                            if (waiverBlock is not null) return new ApiResponses().BadRequestResult(waiverBlock);
                        }
                    }
                    await _seasonPasses.UpdateReservationStatus(purchaseId, tenantId,
                        req.CheckedIn ? "checked_in" : "reserved",
                        req.CheckedIn ? staffId : null);
                    break;
                default:
                    return new ApiResponses().BadRequestResult("Unknown source.");
            }
            return new ApiResponses().OkResult(new { purchaseId, checkedIn = req.CheckedIn });
        }

        [Authorize(Policy = TenantPermissions.Policy.SalesRedeem)]
        [HttpPut("Admin/EventRiders/Ticket/{purchaseId:guid}/RaceNumber")]
        public async Task<IActionResult> SetRaceNumber(Guid purchaseId, [FromBody] SetRaceNumberRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            // Race number lives only on event_ticket_purchase — passes and
            // season-pass reservations don't carry one.
            var trimmed = string.IsNullOrWhiteSpace(req.RaceNumber) ? null : req.RaceNumber.Trim();
            await _tickets.SetRaceNumber(purchaseId, _tenantContext.TenantId, trimmed);
            return new ApiResponses().OkResult(new { purchaseId, raceNumber = trimmed });
        }

        // Send-now-or-schedule rider messages (SMS or email). When RunAtUtc is
        // null/past the request sends immediately and returns per-row results
        // (the old SendSms path). When RunAtUtc is in the future, we enqueue a
        // scheduled_task row keyed by EventId so the report's "Scheduled" panel
        // can list and cancel it, and the TaskRunner's dispatcher picks it up.
        [Authorize(Policy = TenantPermissions.Policy.SalesRedeem)]
        [HttpPost("Admin/EventRiders/{eventId:guid}/SendMessage")]
        public async Task<IActionResult> SendRiderMessage(Guid eventId, [FromBody] SendRiderMessageRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");

            var channel = (req.Channel ?? "sms").ToLowerInvariant();
            if (channel == "sms" && !_sms.IsConfiguredFor(_tenantContext.Tenant))
            {
                return new ApiResponses().BadRequestResult(
                    "SMS isn't configured for this tenant. Provision Twilio in Settings → SMS.");
            }
            if (channel == "email" && !_emailer.IsConfigured)
            {
                return new ApiResponses().BadRequestResult(
                    "Email isn't configured for this tenant. Add SMTP credentials in app settings.");
            }
            if (channel == "email" && string.IsNullOrWhiteSpace(req.Subject))
            {
                // Subject is optional but blank-stripped to enforce a real title;
                // the handler also defaults if we let it through, but better to
                // tell the admin while they still have the dialog open.
                return new ApiResponses().BadRequestResult("Email subject is required.");
            }

            var ev = await _events.GetById(eventId, _tenantContext.TenantId);
            if (ev is null) return new ApiResponses().NotFoundResult("Event not found.");

            var runAt = req.RunAtUtc?.ToUniversalTime();
            // Treat "now or earlier" as immediate. A 60-second grace window
            // means clock skew between the admin's browser and the server
            // doesn't accidentally enqueue something they meant to send now.
            var isScheduled = runAt.HasValue && runAt.Value > DateTime.UtcNow.AddSeconds(60);

            if (!isScheduled)
            {
                var (sent, skipped) = await SendRiderMessageNow(eventId, req, channel, ev.Title);
                return new ApiResponses().OkResult(new SendRiderMessageResponse
                {
                    Sent = sent, Skipped = skipped.Count, SkippedNames = skipped,
                });
            }

            // Future-scheduled: enqueue and let the TaskRunner pick it up.
            var payload = new Services.Scheduling.Handlers.SendRiderMessagePayload
            {
                EventId = eventId,
                PurchaseIds = req.PurchaseIds,
                Channel = channel,
                Subject = string.IsNullOrWhiteSpace(req.Subject) ? null : req.Subject!.Trim(),
                Body = req.Body,
            };
            var payloadJson = System.Text.Json.JsonSerializer.Serialize(payload);
            Guid? createdBy = Guid.TryParse(User.FindFirst("UserId")?.Value, out var uid) ? uid : null;
            var taskId = await _scheduledTasks.Enqueue(_tenantContext.TenantId, "send_rider_message",
                payloadJson, runAt!.Value, createdBy);
            return new ApiResponses().OkResult(new SendRiderMessageResponse
            {
                ScheduledTaskId = taskId,
                ScheduledRunAtUtc = DateTime.SpecifyKind(runAt.Value, DateTimeKind.Utc),
            });
        }

        // Pulled into a private helper so both the immediate and the scheduled
        // (when the TaskRunner runs it via the handler) paths share the same
        // per-row send logic shape. The TaskRunner uses its own copy in
        // SendRiderMessageHandler — they could be unified later via a shared
        // service.
        private async Task<(int Sent, List<string> Skipped)> SendRiderMessageNow(
            Guid eventId, SendRiderMessageRequest req, string channel, string eventTitle)
        {
            var rows = await _reports.GetEventRiders(_tenantContext.TenantId, eventId);
            var requested = req.PurchaseIds.ToHashSet();
            var targets = rows.Where(r => requested.Contains(r.PurchaseId)).ToList();
            var sent = 0;
            var skipped = new List<string>();
            var tenant = _tenantContext.Tenant;

            foreach (var row in targets)
            {
                bool ok;
                if (channel == "sms")
                {
                    var normalized = TwilioSmsSender.NormalizeE164(row.PurchaserPhone ?? "");
                    if (string.IsNullOrEmpty(normalized))
                    {
                        skipped.Add(row.PurchaserName);
                        continue;
                    }
                    ok = await _sms.Send(tenant, normalized, req.Body);
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(row.PurchaserEmail))
                    {
                        skipped.Add(row.PurchaserName);
                        continue;
                    }
                    var subject = string.IsNullOrWhiteSpace(req.Subject)
                        ? $"Update from {tenant.DisplayName}"
                        : req.Subject!.Trim();
                    var html = BuildRiderMessageHtml(req.Body, tenant.DisplayName, eventTitle);
                    ok = await _emailer.Send(row.PurchaserEmail, subject, html);
                }
                if (ok) sent++;
                else skipped.Add(row.PurchaserName);
            }
            return (sent, skipped);
        }

        // Same shell SendRiderMessageHandler uses — kept inline here so the
        // immediate path doesn't fan out to the scheduling layer just for HTML
        // wrapping. If a third caller needs it, lift into a shared formatter.
        private static string BuildRiderMessageHtml(string plainBody, string tenantName, string eventTitle)
        {
            var escaped = System.Net.WebUtility.HtmlEncode(plainBody).Replace("\n", "<br>");
            return $@"<!doctype html>
<html><body style=""font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; max-width: 600px; margin: 0 auto; padding: 24px; color: #1f2937;"">
    <div style=""font-size: 12px; color: #6b7280; text-transform: uppercase; letter-spacing: 0.5px;"">{System.Net.WebUtility.HtmlEncode(tenantName)}</div>
    <div style=""font-size: 18px; font-weight: 600; margin-top: 4px;"">{System.Net.WebUtility.HtmlEncode(eventTitle)}</div>
    <hr style=""border: none; border-top: 1px solid #e5e7eb; margin: 16px 0;"">
    <div style=""font-size: 15px; line-height: 1.55;"">{escaped}</div>
    <hr style=""border: none; border-top: 1px solid #e5e7eb; margin: 24px 0 16px 0;"">
    <div style=""font-size: 12px; color: #9ca3af;"">Sent from {System.Net.WebUtility.HtmlEncode(tenantName)}. Reply directly to reach the track.</div>
</body></html>";
        }

        // List pending scheduled rider-messages for one event so the admin can
        // see what's queued and cancel any of them. Only 'pending' rows show —
        // succeeded/failed are visible only via debug tools (kept off the
        // report to avoid clutter; can be exposed later if needed).
        [Authorize(Policy = TenantPermissions.Policy.SalesRedeem)]
        [HttpGet("Admin/EventRiders/{eventId:guid}/ScheduledMessages")]
        public async Task<IActionResult> ListScheduledRiderMessages(Guid eventId)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var ev = await _events.GetById(eventId, _tenantContext.TenantId);
            if (ev is null) return new ApiResponses().NotFoundResult("Event not found.");
            var rows = await _scheduledTasks.ListPendingForTenant(_tenantContext.TenantId, eventId);
            var items = rows
                .Where(r => r.Kind == "send_rider_message")
                .Select(r => new ScheduledTaskListItem
                {
                    Id = r.Id,
                    Kind = r.Kind,
                    RunAtUtc = DateTime.SpecifyKind(r.RunAtUtc, DateTimeKind.Utc),
                    Status = r.Status,
                    Summary = ExtractMessageSummary(r.Payload),
                    CreatedAtUtc = DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc),
                    CreatedByUserId = r.CreatedByUserId,
                });
            return new ApiResponses().OkResult(items);
        }

        [Authorize(Policy = TenantPermissions.Policy.SalesRedeem)]
        [HttpPost("Admin/ScheduledMessages/{id:guid}/Cancel")]
        public async Task<IActionResult> CancelScheduledRiderMessage(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            Guid? actorId = Guid.TryParse(User.FindFirst("UserId")?.Value, out var uid) ? uid : null;
            if (!actorId.HasValue) return new ApiResponses().BadRequestResult("Invalid token.");
            var existing = await _scheduledTasks.GetById(id, _tenantContext.TenantId);
            if (existing is null) return new ApiResponses().NotFoundResult("Scheduled task not found.");
            await _scheduledTasks.Cancel(id, _tenantContext.TenantId, actorId.Value);
            return new ApiResponses().OkResult(new { id, status = "cancelled" });
        }

        // Best-effort summary for the listing card: "SMS to 12 (Race Day reminder)".
        // Parses the same SendRiderMessagePayload shape the handler uses.
        private static string? ExtractMessageSummary(string payloadJson)
        {
            try
            {
                var p = System.Text.Json.JsonSerializer.Deserialize<Services.Scheduling.Handlers.SendRiderMessagePayload>(
                    payloadJson, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (p is null) return null;
                var channelLabel = p.Channel == "email" ? "Email" : "Text";
                var preview = (p.Body ?? string.Empty).Trim();
                if (preview.Length > 60) preview = preview[..57] + "…";
                return $"{channelLabel} to {p.PurchaseIds.Count} — {preview}";
            }
            catch { return null; }
        }

        // CSV shaped to match MyLaps Trackside's import template — Number,
        // FirstName, LastName, Class, Hometown, Email, Phone. Only race-entry
        // rows are exported; spectator passes and pure pass purchases aren't
        // riders the timing software needs.
        [Authorize(Policy = TenantPermissions.Policy.ReportsView)]
        [HttpGet("Admin/EventRiders/{eventId:guid}/Export/Trackside")]
        public async Task<IActionResult> ExportTrackside(Guid eventId)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var ev = await _events.GetById(eventId, _tenantContext.TenantId);
            if (ev is null) return new ApiResponses().NotFoundResult("Event not found.");

            var rows = (await _reports.GetEventRiders(_tenantContext.TenantId, eventId))
                .Where(r => r.Source == "event_ticket" && r.TierKind == "race_entry")
                .ToList();

            var csv = new System.Text.StringBuilder();
            csv.AppendLine("Number,FirstName,LastName,Class,Hometown,Email,Phone");
            foreach (var r in rows)
            {
                var (first, last) = SplitName(r.FirstName, r.LastName, r.PurchaserName);
                var number = !string.IsNullOrWhiteSpace(r.RaceNumber) ? r.RaceNumber : (r.UserRaceNumber ?? "");
                csv.AppendLine(string.Join(',', new[] {
                    CsvEscape(number),
                    CsvEscape(first),
                    CsvEscape(last),
                    CsvEscape(r.ItemName),
                    CsvEscape(r.Hometown ?? ""),
                    CsvEscape(r.PurchaserEmail),
                    CsvEscape(r.PurchaserPhone ?? ""),
                }));
            }
            var safeTitle = string.Concat((ev.Title ?? "event").Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'));
            var filename = $"trackside-{safeTitle}-{DateTime.UtcNow:yyyyMMdd}.csv";
            return File(System.Text.Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", filename);
        }

        // RFC 4180 minimal: quote when the value contains a quote, comma, or
        // line break; double internal quotes.
        private static string CsvEscape(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var needsQuoting = value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
            if (!needsQuoting) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        // ── Daily Events ────────────────────────────────────────────────────
        // Caller passes the UTC half-open range for one local day in the tenant's
        // timezone (frontend computes that — matches the Summary endpoint pattern).
        [Authorize(Policy = TenantPermissions.Policy.ReportsView)]
        [HttpGet("Admin/DailyEvents")]
        public async Task<IActionResult> GetDailyEvents(
            [FromQuery] DateTime fromUtc,
            [FromQuery] DateTime toUtc,
            [FromQuery] string? localDate = null)
        {
            if (toUtc <= fromUtc)
            {
                return new ApiResponses().BadRequestResult("toUtc must be after fromUtc.");
            }
            var rows = await _reports.GetEventsInRange(_tenantContext.TenantId, fromUtc, toUtc);
            var resp = new DailyEventReportResponse
            {
                LocalDate = localDate ?? string.Empty,
                Rows = rows.Select(r => new DailyEventRowDto
                {
                    EventId = r.EventId,
                    Title = r.Title,
                    EventTypeName = r.EventTypeName ?? string.Empty,
                    StartsAtUtc = DateTime.SpecifyKind(r.StartsAtUtc, DateTimeKind.Utc),
                    EndsAtUtc = DateTime.SpecifyKind(r.EndsAtUtc, DateTimeKind.Utc),
                    AllDay = r.AllDay,
                    Capacity = r.Capacity,
                    Status = r.Status,
                    Registered = r.Registered,
                    CheckedIn = r.CheckedIn,
                    RevenueCents = r.RevenueCents,
                }).ToList(),
            };
            return new ApiResponses().OkResult(resp);
        }

        // ── Check-In lookup ─────────────────────────────────────────────────
        // Resolves any redemption token (pass / event_ticket / season_pass purchase)
        // to the rider, returning today + future registrations across all three sources
        // plus waiver / membership gating flags. Gate staff scan a QR, we hand back
        // everything they need to make a check-in decision in one call.
        [Authorize(Policy = TenantPermissions.Policy.SalesRedeem)]
        [HttpGet("Admin/CheckInLookup")]
        public async Task<IActionResult> CheckInLookup(
            [FromQuery] Guid token,
            [FromQuery] DateTime fromUtc,
            [FromQuery] DateTime toUtc)
        {
            if (token == Guid.Empty) return new ApiResponses().BadRequestResult("Token is required.");
            if (toUtc <= fromUtc) return new ApiResponses().BadRequestResult("toUtc must be after fromUtc.");

            var data = await _reports.LookupCheckInByToken(_tenantContext.TenantId, token, fromUtc, toUtc);
            if (data is null) return new ApiResponses().NotFoundResult("No registration found for that token.");

            // Waiver gating: tenant has an active waiver AND any of the rider's
            // today registrations is on an event that requires it.
            var activeWaiver = await _waivers.GetActive(_tenantContext.TenantId);
            data.RequiresWaiver = false;
            data.WaiverSigned = false;
            if (activeWaiver is not null && data.UserId.HasValue)
            {
                var sig = await _waivers.GetSignature(data.UserId.Value, activeWaiver.Id);
                data.WaiverSigned = sig is not null;
                // Look up each today-registration's event to see if it requires a waiver.
                // Check-in is rider-side, so we look at the rider waiver flag here.
                foreach (var r in data.TodayRegistrations)
                {
                    var ev = await _events.GetById(r.EventId, _tenantContext.TenantId);
                    if (ev is not null && ev.RequiresRiderWaiver) { data.RequiresWaiver = true; break; }
                }
            }

            // Membership gating: surface the flag whenever the tenant requires
            // membership for ANY guarded purchase kind. The UI shows a warning
            // when required-and-not-active so staff don't check in a lapsed member.
            var t = _tenantContext.Tenant;
            data.RequiresMembership = t.MembershipEnabled && t.MembershipPriceCents > 0
                && (t.MembershipRequiredForRiders || t.MembershipRequiredForSpectators);
            data.MembershipName = t.MembershipName;
            data.MembershipActive = false;
            if (data.RequiresMembership && data.UserId.HasValue)
            {
                var active = await _memberships.GetActive(data.UserId.Value, t.Id, DateTime.UtcNow);
                data.MembershipActive = active is not null;
            }

            // Map to wire DTO with explicit UTC kinds (Dapper hands back unspecified).
            var resp = new CheckInLookupResponse
            {
                UserId = data.UserId,
                PurchaserName = data.PurchaserName,
                PurchaserEmail = data.PurchaserEmail,
                PurchaserPhone = data.PurchaserPhone,
                PhotoDataUrl = data.PhotoDataUrl,
                MatchedTokenKind = data.MatchedTokenKind,
                RequiresWaiver = data.RequiresWaiver,
                WaiverSigned = data.WaiverSigned,
                RequiresMembership = data.RequiresMembership,
                MembershipActive = data.MembershipActive,
                MembershipName = data.MembershipName,
                TodayRegistrations = data.TodayRegistrations.Select(MapRegistration).ToList(),
                FutureRegistrations = data.FutureRegistrations.Select(MapRegistration).ToList(),
            };
            return new ApiResponses().OkResult(resp);
        }

        private static CheckInRegistrationDto MapRegistration(CheckInRegistration r) => new()
        {
            Id = r.Id,
            Source = r.Source,
            EventId = r.EventId,
            EventTitle = r.EventTitle,
            EventStartsAtUtc = DateTime.SpecifyKind(r.EventStartsAtUtc, DateTimeKind.Utc),
            EventEndsAtUtc = DateTime.SpecifyKind(r.EventEndsAtUtc, DateTimeKind.Utc),
            ItemName = r.ItemName,
            Status = r.Status,
            CheckedIn = r.CheckedIn,
            CheckedInAtUtc = r.CheckedInAtUtc.HasValue
                ? DateTime.SpecifyKind(r.CheckedInAtUtc.Value, DateTimeKind.Utc) : null,
            RedemptionToken = r.RedemptionToken,
        };

        // Trackside imports want first/last separately. Prefer the user-row
        // values when the rider has an account; otherwise best-effort split of
        // the typed-in purchaser name (last token = last name, rest = first).
        private static (string First, string Last) SplitName(string? first, string? last, string fullName)
        {
            if (!string.IsNullOrWhiteSpace(first) || !string.IsNullOrWhiteSpace(last))
            {
                return (first?.Trim() ?? "", last?.Trim() ?? "");
            }
            var parts = (fullName ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return ("", "");
            if (parts.Length == 1) return (parts[0], "");
            return (string.Join(' ', parts.Take(parts.Length - 1)), parts[^1]);
        }
    }
}
