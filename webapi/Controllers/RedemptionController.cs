using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Redemption;
using webapi.Helpers;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = TenantPermissions.Policy.SalesRedeem)]
    public class RedemptionController : ControllerBase
    {
        private readonly IEventTicketPurchaseRepository _tickets;
        private readonly IEventExtraRepository _extras;
        private readonly IUserRepository _users;
        private readonly IEventRepository _events;
        private readonly IWaiverRepository _waivers;
        private readonly Services.Waivers.IWaiverCheckInGate _waiverGate;
        private readonly Services.Riders.IRiderPhotos _riderPhotos;
        private readonly ITenantContext _tenantContext;
        private readonly Services.Audit.IAuditLogger _audit;
        private readonly ISeasonPassRepository _passes;
        private readonly IWaiverSignRequestRepository _signRequests;
        private readonly ISmtpEmailer _emailer;
        private readonly IConfiguration _config;
        private readonly Services.Riders.IRiderIdVerification _idVerification;
        private readonly IRiderDayBands _dayBands;

        public RedemptionController(
            IEventTicketPurchaseRepository tickets,
            IEventExtraRepository extras,
            IUserRepository users,
            IEventRepository events,
            IWaiverRepository waivers,
            Services.Waivers.IWaiverCheckInGate waiverGate,
            ITenantContext tenantContext,
            Services.Audit.IAuditLogger audit,
            ISeasonPassRepository passes,
            IWaiverSignRequestRepository signRequests,
            ISmtpEmailer emailer,
            IConfiguration config,
            Services.Riders.IRiderIdVerification idVerification,
            Services.Riders.IRiderPhotos riderPhotos,
            IRiderDayBands dayBands)
        {
            _dayBands = dayBands;
            _idVerification = idVerification;
            _riderPhotos = riderPhotos;
            _audit = audit;
            _passes = passes;
            _signRequests = signRequests;
            _emailer = emailer;
            _config = config;
            _tickets = tickets;
            _extras = extras;
            _users = users;
            _events = events;
            _waivers = waivers;
            _waiverGate = waiverGate;
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

            // A multi-day event (a training camp) admits the same ticket once per DAY, so
            // 'redeemed' from an earlier day must not block today. Per-day truth lives in
            // event_ticket_attendance; the status flag still marks the first admission so
            // existing reports and the roster keep working unchanged. Single-day events take
            // the original path exactly.
            var isMultiDay = IsMultiDayEvent(preview);

            if (preview.Status == "redeemed" && !isMultiDay)
            {
                return new ApiResponses().BadRequestResult("Already redeemed.");
            }
            if (preview.Status != "paid" && !(isMultiDay && preview.Status == "redeemed"))
            {
                return new ApiResponses().BadRequestResult($"Cannot redeem a purchase with status '{preview.Status}'.");
            }

            if (!preview.IsRedeemableToday)
            {
                return new ApiResponses().BadRequestResult(preview.NotRedeemableReason ?? "This purchase is not redeemable today.");
            }

            // Waiver gate: a required event waiver can't be skipped at check-in.
            RiderDayBandCheckIn? bandRule = null;
            var ticketRow = await _tickets.GetById(preview.PurchaseId, _tenantContext.TenantId);
            if (ticketRow is not null)
            {
                var waiverDecision = await _waiverGate.EvaluateTicket(_tenantContext.TenantId, ticketRow);
                if (waiverDecision.BlockReason is not null)
                    return GateBlockResult.Result(waiverDecision, "ticket", ticketRow.RedemptionToken, ticketRow.Id);
                // The day band (Script0313): the band the rider wears today covers this admission; a track that
                // requires a band at check-in refuses a rider without one (scan them in from the order screen).
                if (_tenantContext.Tenant.WristbandsEnabled)
                {
                    bandRule = await _dayBands.ForCheckIn(await _dayBands.ForTicket(ticketRow), await _dayBands.TicketDate(ticketRow));
                    if (bandRule.Required && waiverDecision.RiderAudience) return bandRule.Refusal!;
                }
            }

            var staffId = TryGetStaffUserId();
            var nowUtc = DateTime.UtcNow;
            var tenantId = _tenantContext.TenantId;

            if (isMultiDay)
            {
                // One admission per local day. A second scan on the same day is refused here
                // rather than silently double-counting the roster.
                var todayLocal = DateOnly.FromDateTime(
                    TimeZoneInfo.ConvertTimeFromUtc(nowUtc, ResolveTenantTimeZone()).Date);
                var recorded = await _tickets.TryRecordAttendance(preview.PurchaseId, tenantId, todayLocal, staffId);
                if (!recorded)
                {
                    return new ApiResponses().BadRequestResult("Already checked in today.");
                }
                // Flip 'paid' -> 'redeemed' on the FIRST day only, so redeemed_at_utc keeps
                // meaning "first admitted" and a later day never rewrites it.
                if (preview.Status == "paid")
                {
                    if (staffId.HasValue) await _tickets.TryMarkRedeemed(preview.PurchaseId, tenantId, staffId.Value, nowUtc);
                    else                  await _tickets.UpdateStatus(preview.PurchaseId, "redeemed");
                }
            }
            else if (staffId.HasValue) await _tickets.MarkRedeemed(preview.PurchaseId, tenantId, staffId.Value, nowUtc);
            else                       await _tickets.UpdateStatus(preview.PurchaseId, "redeemed");

            preview.Status = "redeemed";
            if (bandRule is not null) await _dayBands.CompleteTicketCheckIn(bandRule, preview.PurchaseId, staffId);
            return new ApiResponses().OkResult(preview);
        }

        // Event-wide check-in roster: every paid/redeemed attendee for one event with the
        // attributes the operator app filters on (race class / gate fee, rider vs spectator,
        // checked-in state, race number). Powers the live roster view and the offline roster
        // snapshot. Event- and tenant-scoped; SalesRedeem (class-level) gates it.
        // onDate (yyyy-MM-dd, tenant-local) picks which day of a multi-day camp the roster is
        // for; omit it and a camp defaults to today, which is what someone standing at the gate
        // wants. A single-day event ignores it entirely and keeps reporting status-based
        // check-in, so existing operator-app calls are unaffected.
        [HttpGet("Roster/{eventId:guid}")]
        public async Task<IActionResult> Roster(Guid eventId, [FromQuery] DateOnly? onDate = null)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");

            var ev = await _events.GetById(eventId, _tenantContext.TenantId);
            if (ev is null) return new ApiResponses().NotFoundResult("Event not found.");

            var tz = ResolveTenantTimeZone();
            var startLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(ev.StartsAt, DateTimeKind.Utc), tz).Date;
            var endLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(ev.EndsAt, DateTimeKind.Utc), tz).Date;

            DateOnly? day = null;
            if (endLocal > startLocal)
            {
                var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).Date);
                day = onDate ?? today;
                // Clamp to the camp's own days so a typo can't ask for a date the camp never runs.
                var first = DateOnly.FromDateTime(startLocal);
                var last = DateOnly.FromDateTime(endLocal);
                if (day < first) day = first;
                if (day > last) day = last;
            }

            var rows = await _tickets.ListEventRoster(eventId, _tenantContext.TenantId, day);
            return new ApiResponses().OkResult(rows);
        }

        // Sync admissions an operator device made offline. Idempotent and first-to-sync-wins:
        // each item flips paid -> redeemed only if still paid, so a re-sent batch is a no-op
        // and a person two devices both admitted offline resolves to one admit with the other
        // flagged as a conflict for staff to reconcile.
        //
        // The waiver/registration gate and the event-date window ARE re-run server-side: the offline
        // roster can be stale (or a tampered/replayed client could send anything), so the server is the
        // authority. The client-supplied AdmittedAtUtc is preserved as the redemption time but clamped
        // to a sane range so it can't be a bogus year-0001 or future timestamp. Event/tenant scope
        // rides each token lookup.
        [HttpPost("AdmitBatch")]
        public async Task<IActionResult> AdmitBatch([FromBody] BatchAdmitRequest request)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var staffId = TryGetStaffUserId();
            if (staffId is null) return new ApiResponses().BadRequestResult("No authenticated user.");

            var items = request?.Items ?? new List<BatchAdmitItem>();
            if (items.Count > 500)
            {
                return new ApiResponses().BadRequestResult("Too many admissions in one batch (max 500). Sync in smaller chunks.");
            }

            var tenantId = _tenantContext.TenantId;
            var nowUtc = DateTime.UtcNow;
            var results = new List<BatchAdmitResult>(items.Count);

            foreach (var item in items)
            {
                var result = new BatchAdmitResult { ClientRef = item.ClientRef, RedemptionToken = item.RedemptionToken };

                var row = await _tickets.GetByRedemptionToken(item.RedemptionToken, tenantId);
                if (row is null)
                {
                    result.Outcome = "not_found";
                    results.Add(result);
                    continue;
                }
                // A multi-day event (a camp) admits once per DAY, so 'redeemed' from an earlier
                // day is not a duplicate and must fall through to the per-day check below.
                var tzForDay = ResolveTenantTimeZone();
                var rowStartLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(row.EventStartsAt, DateTimeKind.Utc), tzForDay).Date;
                var rowEndLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(row.EventEndsAt, DateTimeKind.Utc), tzForDay).Date;
                var rowIsMultiDay = rowEndLocal > rowStartLocal;

                if (row.Status == "redeemed" && !rowIsMultiDay)
                {
                    // Already in: your own re-sync is idempotent success; another device that
                    // got there first is a conflict to surface.
                    result.RedeemedByUserId = row.RedeemedByUserId;
                    result.RedeemedAtUtc = row.RedeemedAtUtc;
                    result.Outcome = row.RedeemedByUserId == staffId.Value ? "admitted" : "conflict";
                    results.Add(result);
                    continue;
                }
                if (row.Status != "paid" && !(rowIsMultiDay && row.Status == "redeemed"))
                {
                    result.Outcome = "not_admissible";
                    results.Add(result);
                    continue;
                }

                // Event-date window: admit only within the event's check-in window (event date ±1 day
                // in the tenant's timezone), so a stale device can't admit against an event months off.
                if (!IsWithinGateWindow(row.EventStartsAt, row.EventEndsAt))
                {
                    result.Outcome = "blocked";
                    result.BlockReason = "This admission is outside the event's check-in window.";
                    results.Add(result);
                    continue;
                }

                // Re-run the waiver / registration gate authoritatively. GetByRedemptionToken doesn't
                // carry the waiver columns, so load the full row for the gate.
                var full = await _tickets.GetById(row.Id, tenantId);
                if (full is not null)
                {
                    var decision = await _waiverGate.EvaluateTicket(tenantId, full);
                    if (decision.BlockReason is not null)
                    {
                        result.Outcome = "blocked";
                        result.BlockReason = decision.BlockReason;
                        // Structured, so the operator app can tell "check their ID" from a waiver block.
                        result.BlockCode = GateBlockResult.Code(decision);
                        result.RiderName = decision.RiderName;
                        results.Add(result);
                        continue;
                    }
                }

                // Clamp the offline timestamp to [purchase time, now]; fall back to now if bogus.
                var admittedAt = ClampAdmitTime(item.AdmittedAtUtc, DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc), nowUtc);

                // Multi-day event (a camp): admission is per DAY, so the paid -> redeemed flip
                // can't arbitrate. Attendance for the admitted day decides instead, keeping the
                // same first-to-sync-wins semantics: whoever records the day wins, a second
                // device syncing the same day is the duplicate.
                if (rowIsMultiDay)
                {
                    var admittedDay = DateOnly.FromDateTime(
                        TimeZoneInfo.ConvertTimeFromUtc(admittedAt, tzForDay).Date);
                    var recordedDay = await _tickets.TryRecordAttendance(row.Id, tenantId, admittedDay, staffId.Value);
                    if (recordedDay)
                    {
                        // First day also flips the status so reports and the roster still see
                        // this ticket as used; later days leave redeemed_at_utc alone.
                        await _tickets.TryMarkRedeemed(row.Id, tenantId, staffId.Value, admittedAt);
                        result.Outcome = "admitted";
                        await LinkWornBand(full, admittedDay, staffId.Value);
                        result.RedeemedByUserId = staffId.Value;
                        result.RedeemedAtUtc = admittedAt;
                    }
                    else
                    {
                        // That day is already recorded. Same semantics as the single-day path:
                        // your own re-sync is idempotent success, another device is a conflict.
                        var freshDay = await _tickets.GetByRedemptionToken(item.RedemptionToken, tenantId);
                        result.RedeemedByUserId = freshDay?.RedeemedByUserId;
                        result.RedeemedAtUtc = freshDay?.RedeemedAtUtc;
                        result.Outcome = freshDay?.RedeemedByUserId == staffId.Value ? "admitted" : "conflict";
                    }
                    results.Add(result);
                    continue;
                }

                var flipped = await _tickets.TryMarkRedeemed(row.Id, tenantId, staffId.Value, admittedAt);
                if (flipped)
                {
                    result.Outcome = "admitted";
                    await LinkWornBand(full, null, staffId.Value);
                    result.RedeemedByUserId = staffId.Value;
                    result.RedeemedAtUtc = admittedAt;
                }
                else
                {
                    // Lost a race between the read and the write: re-read to report who holds it.
                    var fresh = await _tickets.GetByRedemptionToken(item.RedemptionToken, tenantId);
                    result.RedeemedByUserId = fresh?.RedeemedByUserId;
                    result.RedeemedAtUtc = fresh?.RedeemedAtUtc;
                    result.Outcome = fresh?.RedeemedByUserId == staffId.Value ? "admitted" : "conflict";
                }
                results.Add(result);
            }

            // Bulk admissions are audited; the single gate scan deliberately is not. A busy gate
            // scans thousands of times a day and logging each one would bury the money signal the
            // rest of the audit trail exists for. A batch is different: it is rare, it is usually
            // an offline device syncing back, and admitting hundreds of people in one call is the
            // shape worth being able to review after the fact.
            var admitted = results.Count(r => r.Outcome == "admitted");
            if (admitted > 0)
            {
                await _audit.Log(
                    "redemption.admit_batch",
                    $"Admitted {admitted} of {results.Count} entries in one batch",
                    targetKind: "event_ticket_batch",
                    targetId: null,
                    tenantId: tenantId,
                    metadata: new
                    {
                        submitted = results.Count,
                        admitted,
                        conflicts = results.Count(r => r.Outcome == "conflict"),
                    });
            }

            return new ApiResponses().OkResult(new BatchAdmitResponse { Results = results });
        }

        // An offline admission synced: the band the rider wears that day covers it too (Script0313). Never a
        // refusal here: the rider is already in, and a device that was offline couldn't have scanned a band.
        private async Task LinkWornBand(Services.Repositories.Data.PaymentData.EventTicketPurchase? ticket, DateOnly? day, Guid staffId)
        {
            if (ticket is null || !_tenantContext.Tenant.WristbandsEnabled) return;
            var rule = await _dayBands.ForCheckIn(await _dayBands.ForTicket(ticket), day ?? await _dayBands.TicketDate(ticket));
            await _dayBands.CompleteTicketCheckIn(rule, ticket.Id, staffId);
        }

        // Whether today (tenant tz) falls within the event's date window widened by one day on each
        // side, to allow legitimate early gate-open the night before and next-morning teardown.
        private bool IsWithinGateWindow(DateTime eventStartsAtUtc, DateTime eventEndsAtUtc)
        {
            var tz = ResolveTenantTimeZone();
            var today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).Date;
            var start = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(eventStartsAtUtc, DateTimeKind.Utc), tz).Date.AddDays(-1);
            var end = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(eventEndsAtUtc, DateTimeKind.Utc), tz).Date.AddDays(1);
            return today >= start && today <= end;
        }

        // Keep the offline admit time honest: use it only when it falls between the purchase and now;
        // otherwise (year-0001 default, future, or pre-purchase) record the sync time.
        private static DateTime ClampAdmitTime(DateTime clientUtc, DateTime purchaseCreatedUtc, DateTime nowUtc)
        {
            var t = DateTime.SpecifyKind(clientUtc, DateTimeKind.Utc);
            if (t < purchaseCreatedUtc || t > nowUtc) return nowUtc;
            return t;
        }

        // Gate lookup by name or email, for the rider who shows up with a dead phone, a lost email, or
        // a ticket someone else bought for them. Returns the orders coming through the gate TODAY that
        // match; picking one feeds its token straight into Order below, so the rest of check-in
        // (items, waivers, bulk redeem) is identical to a QR scan.
        //
        // Two guards make this safe to hand to gate staff: it only sees events whose check-in window is
        // open today, and it requires a real search term (a 3-char minimum, so it can't be used to walk
        // the customer list). SalesRedeem (class-level) still gates it.
        //
        // Results carry a Kind: "ticket" rows anchor an event order (Order/{token}), "season_pass"
        // rows are passes valid today whose token opens the pass card (SeasonPass/Pass/{token}).
        // Each kind is capped at GateSearchLimitPerKind.
        private const int GateSearchLimitPerKind = 25;

        [HttpGet("Search")]
        public async Task<IActionResult> Search([FromQuery] string q)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");

            var query = (q ?? string.Empty).Trim();
            if (query.Length < 3)
            {
                return new ApiResponses().BadRequestResult("Type at least 3 characters of a name or email.");
            }

            // Today in the tenant's timezone, expressed as a UTC interval so the SQL stays timezone-free.
            var tz = ResolveTenantTimeZone();
            var todayLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).Date;
            var todayStartUtc = TimeZoneInfo.ConvertTimeToUtc(todayLocal, tz);
            var todayEndUtc = TimeZoneInfo.ConvertTimeToUtc(todayLocal.AddDays(1).AddTicks(-1), tz);
            // The pass card's day bound is exclusive (next local midnight); the pass search shares its rule.
            var tomorrowStartUtc = TimeZoneInfo.ConvertTimeToUtc(todayLocal.AddDays(1), tz);

            // Season pass holders come back in the same list: a pass holder with a dead phone is just
            // as common at the gate as a ticket holder, and staff shouldn't have to know which one
            // the person bought before they can look them up. The two queries are independent
            // (DbHelper opens a connection per call), so run them side by side.
            var ticketTask = _tickets.SearchForGate(
                _tenantContext.TenantId, query, todayStartUtc, todayEndUtc, limit: GateSearchLimitPerKind);
            var passTask = _passes.SearchForGate(
                _tenantContext.TenantId, query, todayLocal, todayStartUtc, tomorrowStartUtc, limit: GateSearchLimitPerKind);
            await Task.WhenAll(ticketTask, passTask);
            var rows = ticketTask.Result;
            var passRows = passTask.Result;

            var results = rows.Select(r => new webapi.Controllers.API.Data.Redemption.GateSearchResult
            {
                Kind = webapi.Controllers.API.Data.Redemption.GateSearchResult.KindTicket,
                DisplayName = !string.IsNullOrWhiteSpace(r.RiderNames) ? r.RiderNames! : r.PurchaserName,
                Holds = r.EventTitle,
                EventId = r.EventId,
                EventTitle = r.EventTitle,
                EventStartsAtUtc = DateTime.SpecifyKind(r.EventStartsAt, DateTimeKind.Utc),
                PurchaserName = r.PurchaserName,
                PurchaserEmail = r.PurchaserEmail,
                AccountEmail = r.AccountEmail,
                AnchorToken = r.AnchorToken,
                ItemCount = r.ItemCount,
                RedeemedCount = r.RedeemedCount,
                RiderNames = r.RiderNames,
            }).Concat(passRows.Select(p => new webapi.Controllers.API.Data.Redemption.GateSearchResult
            {
                Kind = webapi.Controllers.API.Data.Redemption.GateSearchResult.KindSeasonPass,
                DisplayName = p.HolderName ?? p.PurchaserName,
                Holds = p.ProductName,
                PurchaserName = p.PurchaserName,
                PurchaserEmail = p.PurchaserEmail,
                AccountEmail = p.AccountEmail,
                AnchorToken = p.RedemptionToken,
                // A pass is one admission per day: "1 item, redeemed if already in today" keeps
                // the result chips meaning the same thing for both kinds.
                ItemCount = 1,
                RedeemedCount = p.CheckedInToday ? 1 : 0,
                ProductKind = p.ProductKind,
                CreditsRemaining = p.CreditsRemaining,
                ValidToDate = DateOnly.FromDateTime(p.ValidToDate),
            })).ToList();

            return new ApiResponses().OkResult(results);
        }

        // Scan-once-redeem-many: given any token the rider owns for an event, surface
        // every ticket + add-on that SAME purchaser holds for that SAME event, across
        // however many orders they placed, so the gate worker can check them all in from
        // one scan. Scope is bounded to one event and one purchaser (never cross-event,
        // never another buyer); redeeming still requires authenticated SalesRedeem staff.
        [HttpGet("Order/{token:guid}")]
        public async Task<IActionResult> Order(Guid token)
        {
            var tenantId = _tenantContext.TenantId;
            var anchor = await ResolveAnchor(token, tenantId);
            if (anchor is null)
            {
                return new ApiResponses().NotFoundResult("No purchase found for this token in your tenant.");
            }

            var resp = new webapi.Controllers.API.Data.Redemption.OrderLookupResponse
            {
                StripePaymentIntentId = null,   // no longer meaningful: the scope is event+purchaser, not one PI
                PurchaserName = anchor.PurchaserName,
                PurchaserEmail = anchor.PurchaserEmail,
                RequireIdAtCheckin = _tenantContext.Tenant.RequireIdAtCheckin,
            };
            // The birthdate-check policy as the gate enforces it (fresh, not the resolution cache).
            (resp.BirthdateVerifyMode, resp.BirthdateVerifyValidDays) = await _waiverGate.BirthdatePolicy(tenantId);

            var redeemerIds = new HashSet<Guid>();
            var tz = ResolveTenantTimeZone();
            var todayInTenant = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).Date;

            if (anchor.EventId.HasValue)
            {
                var ticketRows = await _tickets.ListByEventForPurchaser(
                    anchor.EventId.Value, tenantId, anchor.PurchaserUserId, anchor.PurchaserEmail);
                // The waiver gate's verdict per ticket, decided once with the same inputs redeem uses;
                // both the Items (what can be selected and admitted) and the Waivers panel read it.
                var decisions = await _waiverGate.EvaluateTickets(tenantId, ticketRows);
                var decisionById = new Dictionary<Guid, Services.Waivers.WaiverGateDecision>();
                for (var i = 0; i < ticketRows.Count; i++) decisionById[ticketRows[i].Id] = decisions[i];
                foreach (var t in ticketRows)
                {
                    var s = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(t.EventStartsAt, DateTimeKind.Utc), tz).Date;
                    var e = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(t.EventEndsAt, DateTimeKind.Utc), tz).Date;
                    var ok = todayInTenant >= s && todayInTenant <= e;
                    var windowReason = ok ? null : (todayInTenant < s ? $"Event starts {s:yyyy-MM-dd}." : $"Event ended {e:yyyy-MM-dd}.");
                    // A rider whose registration isn't finished can't be checked in (the gate blocks it),
                    // so mark it non-redeemable with a clear reason instead of letting staff select it and
                    // hit a redeem error. Priority: window → status → registration → waiver (the gate's
                    // own verdict, e.g. a minor's self-signed waiver).
                    var waiverBlock = decisionById[t.Id].BlockReason;
                    var redeemable = ok && t.Status == "paid" && t.RegistrationComplete && waiverBlock is null;
                    var reason = !ok ? windowReason
                        : t.Status != "paid" ? $"Status is '{t.Status}'."
                        : !t.RegistrationComplete ? "Registration not finished — rider details and waiver still needed."
                        : waiverBlock;
                    resp.Items.Add(new webapi.Controllers.API.Data.Redemption.OrderItem
                    {
                        Kind = "event_ticket",
                        TicketKind = t.TierKind,
                        Audience = t.TierAudience,
                        PurchaseId = t.Id,
                        RedemptionToken = t.RedemptionToken,
                        ItemName = $"{t.EventTitle} — {t.TierName}",
                        AmountCents = t.AmountCents,
                        Status = t.Status,
                        IsRedeemableToday = redeemable,
                        NotRedeemableReason = reason,
                        RedeemedAtUtc = t.RedeemedAtUtc.HasValue ? DateTime.SpecifyKind(t.RedeemedAtUtc.Value, DateTimeKind.Utc) : null,
                        RegistrationComplete = t.RegistrationComplete,
                        AttendeeName = BuildAttendeeName(t.RiderFirstName, t.RiderLastName),
                        SignedByParent = t.SignedByParent,
                        GuardianName = t.SignedByParent ? t.GuardianName : null,
                    });
                    if (t.RedeemedByUserId.HasValue) redeemerIds.Add(t.RedeemedByUserId.Value);
                }

                var extraRows = await _extras.ListByEventForPurchaser(
                    anchor.EventId.Value, tenantId, anchor.PurchaserUserId, anchor.PurchaserEmail);
                foreach (var x in extraRows)
                {
                    resp.Items.Add(BuildExtraItem(x));
                    if (x.RedeemedByUserId.HasValue) redeemerIds.Add(x.RedeemedByUserId.Value);
                }

                // Per-person waiver status for the whole order. This is what makes an unsigned
                // attendee impossible to miss at the gate; redeem is blocked server-side regardless.
                resp.Waivers = await BuildWaiverPanel(tenantId, anchor.EventId.Value, anchor, decisionById);
                // Each person's band for the day (Script0313): worn already (a pass, another ticket), or the band
                // field the people panel shows them ("Admit and band").
                if (_tenantContext.Tenant.WristbandsEnabled)
                    foreach (var w in resp.Waivers)
                        if ((w.PhotoPurchaseId ?? w.PurchaseIds.FirstOrDefault()) is Guid bandTicketId && bandTicketId != Guid.Empty
                            && await _tickets.GetById(bandTicketId, tenantId) is { } bandTicket)
                        {
                            var subject = await _dayBands.ForTicket(bandTicket);
                            if (await _dayBands.BandFor(subject, await _dayBands.TicketDate(bandTicket)) is { } worn)
                            {
                                w.BandCode = worn.Code;
                                w.BandIssuedAtLocal = _dayBands.IssuedTime(worn);
                            }
                        }
                resp.WaiverRequiredCount = resp.Waivers.Count(w => w.WaiverRequired);
                resp.WaiverSignedCount = resp.Waivers.Count(w => w.WaiverRequired && w.WaiverSigned);
                resp.WaiverMissingCount = resp.Waivers.Count(w => w.WaiverRequired && !w.WaiverSigned);
            }
            else if (anchor.SoloExtra is not null)
            {
                // No-event add-on (counter merch): only the scanned row is in scope.
                var named = await _extras.GetPurchaseWithProduct(anchor.SoloExtra.Id, tenantId);
                resp.Items.Add(named is not null ? BuildExtraItem(named) : BuildExtraItem(anchor.SoloExtra));
                if (anchor.SoloExtra.RedeemedByUserId.HasValue) redeemerIds.Add(anchor.SoloExtra.RedeemedByUserId.Value);
            }

            resp.TotalAmountCents = resp.Items.Sum(i => i.AmountCents);

            // Resolve redeemer names, then stamp them onto already-redeemed items.
            var staffById = new Dictionary<Guid, string>();
            foreach (var id in redeemerIds)
            {
                var u = await _users.GetById(id);
                if (u is not null) staffById[id] = $"{u.FirstName} {u.LastName}".Trim();
            }
            foreach (var item in resp.Items)
            {
                if (!item.RedeemedAtUtc.HasValue) continue;
                Guid? byId = item.Kind switch
                {
                    "event_ticket" => (await _tickets.GetById(item.PurchaseId, tenantId))?.RedeemedByUserId,
                    "extras" => (await _extras.GetPurchase(item.PurchaseId))?.RedeemedByUserId,
                    _ => null,
                };
                if (byId.HasValue && staffById.TryGetValue(byId.Value, out var nm)) item.RedeemedByName = nm;
            }

            return new ApiResponses().OkResult(resp);
        }

        [HttpPost("Order/Redeem")]
        public async Task<IActionResult> RedeemBulk([FromBody] webapi.Controllers.API.Data.Redemption.BulkRedeemRequest req)
        {
            var tenantId = _tenantContext.TenantId;

            // Photo-ID gate: when the tenant requires it, the gate worker must attest they
            // checked the rider's ID against the purchaser name before anything redeems.
            if (_tenantContext.Tenant.RequireIdAtCheckin && !req.IdVerified)
            {
                return new ApiResponses().BadRequestResult(
                    "This track requires photo ID verification. Confirm the rider's ID matches the purchaser name, then check the box before redeeming.");
            }

            var anchor = await ResolveAnchor(req.OrderToken, tenantId);
            if (anchor is null)
            {
                return new ApiResponses().NotFoundResult("Order not found.");
            }

            // Authorization scope: only ids in the scanned token's event+purchaser set are
            // redeemable, so a leaked purchase id can't redeem outside this rider's event.
            var allowedTicketIds = new HashSet<Guid>();
            var allowedExtraIds = new HashSet<Guid>();
            // Whether today (tenant tz) is within the event's date window. The single-redeem path
            // enforces this; the bulk path must too, or a crafted request could redeem out-of-window
            // tickets. Computed from the event-scoped ticket rows (all share one event window).
            var eventInWindow = true;
            if (anchor.EventId.HasValue)
            {
                var tz = ResolveTenantTimeZone();
                var todayInTenant = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).Date;
                var ticketRows = await _tickets.ListByEventForPurchaser(anchor.EventId.Value, tenantId, anchor.PurchaserUserId, anchor.PurchaserEmail);
                foreach (var t in ticketRows) allowedTicketIds.Add(t.Id);
                var firstRow = ticketRows.FirstOrDefault();
                if (firstRow is not null)
                {
                    var s = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(firstRow.EventStartsAt, DateTimeKind.Utc), tz).Date;
                    var e = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(firstRow.EventEndsAt, DateTimeKind.Utc), tz).Date;
                    eventInWindow = todayInTenant >= s && todayInTenant <= e;
                }
                foreach (var x in await _extras.ListByEventForPurchaser(anchor.EventId.Value, tenantId, anchor.PurchaserUserId, anchor.PurchaserEmail))
                    allowedExtraIds.Add(x.Id);
            }
            else if (anchor.SoloExtra is not null)
            {
                allowedExtraIds.Add(anchor.SoloExtra.Id);
            }

            var staffId = TryGetStaffUserId();
            var nowUtc = DateTime.UtcNow;
            var resp = new webapi.Controllers.API.Data.Redemption.BulkRedeemResponse();

            // ── 1. Every check, before anything is written ──────────────────────────────────────────
            // Tickets: in the order, in the event's window, paid, and clear at the gate. Their gate decision is
            // kept: the band checks below reuse it rather than asking the gate again.
            var admissible = new List<(Services.Repositories.Data.PaymentData.EventTicketPurchase Ticket, Services.Waivers.WaiverGateDecision Decision)>();
            var extrasToRedeem = new List<Services.Repositories.Data.ExtrasData.EventExtraPurchase>();
            foreach (var entry in req.Items.DistinctBy(i => (i.Kind, i.PurchaseId)))
            {
                try
                {
                    if (entry.Kind == "event_ticket")
                    {
                        if (!allowedTicketIds.Contains(entry.PurchaseId))
                        {
                            resp.Errors.Add("A ticket doesn't belong to this rider's order — skipped."); continue;
                        }
                        if (!eventInWindow)
                        {
                            resp.Errors.Add("This event isn't open for check-in today — skipped."); continue;
                        }
                        var t = await _tickets.GetById(entry.PurchaseId, tenantId);
                        if (t is null) { resp.Errors.Add($"Ticket {entry.PurchaseId} not found."); continue; }
                        if (t.Status == "redeemed") { resp.Errors.Add("A ticket was already redeemed — skipped."); continue; }
                        if (t.Status != "paid") { resp.Errors.Add($"Ticket status is '{t.Status}' — can't redeem."); continue; }
                        var decision = await _waiverGate.EvaluateTicket(tenantId, t);
                        if (decision.BlockReason is not null) { resp.Errors.Add(decision.BlockReason); continue; }
                        admissible.Add((t, decision));
                    }
                    else if (entry.Kind == "extras")
                    {
                        if (!allowedExtraIds.Contains(entry.PurchaseId))
                        {
                            resp.Errors.Add("An add-on doesn't belong to this rider's order — skipped."); continue;
                        }
                        var x = await _extras.GetPurchase(entry.PurchaseId);
                        if (x is null || x.TenantId != tenantId) { resp.Errors.Add($"Add-on {entry.PurchaseId} not found."); continue; }
                        if (x.Status == "redeemed") { resp.Errors.Add("An add-on was already redeemed — skipped."); continue; }
                        if (x.Status != "paid") { resp.Errors.Add($"Add-on status is '{x.Status}' — can't redeem."); continue; }
                        extrasToRedeem.Add(x);
                    }
                    else
                    {
                        resp.Errors.Add($"Unknown kind '{entry.Kind}'.");
                    }
                }
                catch (Exception ex)
                {
                    resp.Errors.Add(ex.Message);
                }
            }

            // Day bands (Script0313): one band per PERSON. "Admit and band" sends a band for a person's tickets;
            // without one, a band the rider already wears today covers them, and a track that requires a band at
            // check-in refuses a rider who has none. A person whose band can't go on is not admitted at all.
            var refused = new HashSet<Guid>();
            var bandPlans = new List<(RiderDayBandSubject Subject, RiderDayBandResult Check, List<Guid> TicketIds)>();
            var codeOwners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // code -> rider key, this request
            var bandedTickets = new HashSet<Guid>();
            foreach (var band in req.Bands)
            {
                var mine = admissible.Where(a => band.PurchaseIds.Contains(a.Ticket.Id)).ToList();
                if (mine.Count == 0 || string.IsNullOrWhiteSpace(band.Code)) continue;
                var subjects = new List<RiderDayBandSubject>();
                foreach (var (ticket, _) in mine) subjects.Add(await _dayBands.ForTicket(ticket));
                var subject = subjects[0];
                var riderKey = $"{subject.Rider.OwnerKey}|{subject.Rider.RiderKey}";
                void Refuse(string why) { resp.Errors.Add(why); foreach (var (t, _) in mine) refused.Add(t.Id); }
                if (subjects.Any(s => $"{s.Rider.OwnerKey}|{s.Rider.RiderKey}" != riderKey))
                {
                    Refuse($"Band {band.Code.Trim()}: one band is for one person; these tickets are for different riders.");
                    continue;
                }
                if (codeOwners.TryGetValue(band.Code.Trim(), out var owner) && owner != riderKey)
                {
                    Refuse($"Band {band.Code.Trim()} is already being put on someone else in this order. Each person needs their own band.");
                    continue;
                }
                var check = await _dayBands.Check(subject, band.Code, null, await _dayBands.TicketDate(mine[0].Ticket), mine[0].Decision);
                if (!check.Ok) { Refuse($"{subject.Name}: {check.Error}"); continue; }
                codeOwners[band.Code.Trim()] = riderKey;
                bandPlans.Add((subject, check, mine.Select(m => m.Ticket.Id).ToList()));
                foreach (var (t, _) in mine) bandedTickets.Add(t.Id);
            }
            // Everyone else being admitted: the band they wear today, and the "required at check-in" rule.
            var checkIns = new Dictionary<Guid, RiderDayBandCheckIn>();
            foreach (var (ticket, decision) in admissible)
            {
                if (refused.Contains(ticket.Id) || bandedTickets.Contains(ticket.Id)) continue;
                var rule = await _dayBands.ForCheckIn(await _dayBands.ForTicket(ticket), await _dayBands.TicketDate(ticket));
                // Riders wear bands; a spectator's ticket is never refused for want of one.
                if (rule.Required && decision.RiderAudience)
                {
                    resp.Errors.Add($"{RiderLabel(ticket)}: {RiderDayBandCheckIn.RequiredMessage}");
                    refused.Add(ticket.Id);
                    continue;
                }
                checkIns[ticket.Id] = rule;
            }

            // ── 2. Writes ───────────────────────────────────────────────────────────────────────────
            var redeemedIds = new HashSet<Guid>();
            foreach (var (t, _) in admissible)
            {
                if (refused.Contains(t.Id)) continue;
                try
                {
                    if (staffId.HasValue) await _tickets.MarkRedeemed(t.Id, tenantId, staffId.Value, nowUtc);
                    else                   await _tickets.UpdateStatus(t.Id, "redeemed");
                    redeemedIds.Add(t.Id);
                    resp.RedeemedCount++;
                    if (checkIns.TryGetValue(t.Id, out var rule)) await _dayBands.CompleteTicketCheckIn(rule, t.Id, staffId);
                }
                catch (Exception ex)
                {
                    resp.Errors.Add(ex.Message);
                }
            }
            foreach (var (subject, check, ticketIds) in bandPlans)
            {
                var admitted = ticketIds.Where(redeemedIds.Contains).ToList();
                if (admitted.Count == 0) continue;
                // Write only: everything was checked above. A refusal now is a race lost since the check; the
                // admissions stand.
                var written = await _dayBands.Write(check, subject, null, admitted, staffId);
                if (written.Ok) resp.BandedCount++;
                else resp.Errors.Add($"{subject.Name} was admitted, but the band didn't go on: {written.Error}");
            }
            foreach (var x in extrasToRedeem)
            {
                try
                {
                    if (staffId.HasValue) await _extras.MarkRedeemed(x.Id, tenantId, staffId.Value, nowUtc);
                    else                   await _extras.UpdateStatus(x.Id, "redeemed");
                    resp.RedeemedCount++;
                }
                catch (Exception ex)
                {
                    resp.Errors.Add(ex.Message);
                }
            }

            // Whole-order redemption: one action admitting every line on an order. Low volume and
            // worth reviewing, unlike the per-entry scan. Records the ID attestation too, since
            // that is a claim the worker made rather than something the system checked.
            if (resp.RedeemedCount > 0)
            {
                await _audit.Log(
                    "redemption.redeem_order",
                    $"Redeemed {resp.RedeemedCount} item(s) on one order at the gate",
                    targetKind: "order",
                    targetId: null,
                    tenantId: tenantId,
                    metadata: new
                    {
                        redeemedCount = resp.RedeemedCount,
                        errorCount = resp.Errors.Count,
                        idVerifiedAttested = req.IdVerified,
                    });
            }

            return new ApiResponses().OkResult(resp);
        }

        // An add-on line for the gate. Named from the catalog when the caller has the joined row
        // (so staff know what to hand over), falling back to a bare "Add-on" for the rare row whose
        // product is gone.
        private static webapi.Controllers.API.Data.Redemption.OrderItem BuildExtraItem(
            Services.Repositories.Data.ExtrasData.EventExtraPurchase x)
        {
            var named = x as Services.Repositories.Data.ExtrasData.EventExtraPurchaseWithProduct;
            var variant = BuildVariantLabel(x.SizeAtPurchase, x.ColorAtPurchase, x.GenderAtPurchase);
            return new webapi.Controllers.API.Data.Redemption.OrderItem
            {
                Kind = "extras",
                PurchaseId = x.Id,
                RedemptionToken = x.RedemptionToken,
                ItemName = string.IsNullOrWhiteSpace(named?.ProductName) ? "Add-on" : named!.ProductName,
                Quantity = x.Quantity,
                VariantLabel = variant,
                AmountCents = x.AmountCents,
                Status = x.Status,
                IsRedeemableToday = x.Status == "paid",
                NotRedeemableReason = x.Status != "paid" ? $"Status is '{x.Status}'." : null,
                RedeemedAtUtc = x.RedeemedAtUtc.HasValue ? DateTime.SpecifyKind(x.RedeemedAtUtc.Value, DateTimeKind.Utc) : null,
            };
        }

        private static string? BuildVariantLabel(string? size, string? color, string? gender)
        {
            var parts = new[] { size, color, gender }.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
            return parts.Count == 0 ? null : string.Join(", ", parts);
        }

        // The order's waiver panel: one entry per attending PERSON, not per line item. A rider with a
        // gate fee plus three race classes is one person here (tickets grouped by registrant), so the
        // gate can answer "who is walking in, and has each of them signed what this event requires".
        //
        // The verdict is NOT re-derived here: it is the waiver gate's own per-ticket decision
        // (IWaiverCheckInGate.EvaluateTicket, the same call redeem makes), aggregated per person.
        // The rows below only add what the decision doesn't carry: names, which document, the
        // signature image, who signed and when. The panel never grants admission.
        private async Task<List<webapi.Controllers.API.Data.Redemption.OrderWaiverAttendee>> BuildWaiverPanel(
            Guid tenantId, Guid eventId, AnchorInfo anchor,
            IReadOnlyDictionary<Guid, Services.Waivers.WaiverGateDecision> decisionById)
        {
            var rows = await _tickets.ListWaiverStatusForPurchaser(
                eventId, tenantId, anchor.PurchaserUserId, anchor.PurchaserEmail);
            var attendees = new List<webapi.Controllers.API.Data.Redemption.OrderWaiverAttendee>();
            if (rows.Count == 0) return attendees;

            var ev = await _events.GetById(eventId, tenantId);
            if (ev is null) return attendees;

            // Document names for display only (the decision carries the ids).
            var active = (ev.RacerWaiverId is null || ev.SpectatorWaiverId is null)
                ? await _waivers.GetActive(tenantId)
                : null;
            var riderDocName = await ResolveWaiverName(ev.RacerWaiverId ?? active?.Id, tenantId, active);
            var spectatorDocName = await ResolveWaiverName(ev.SpectatorWaiverId ?? active?.Id, tenantId, active);

            foreach (var group in rows.GroupBy(r => r.RegistrantId?.ToString() ?? r.PurchaseId.ToString()))
            {
                var tickets = group.ToList();
                var ds = tickets
                    .Select(r => decisionById.TryGetValue(r.PurchaseId, out var d) ? d : null)
                    .OfType<Services.Waivers.WaiverGateDecision>()
                    .ToList();
                var riderAudience = ds.Count > 0
                    ? ds.Any(d => d.RiderAudience)
                    : tickets.Any(r => r.TierKind == "race_entry" || (r.TierKind == "gate_fee" && r.TierAudience == "rider"));

                // Blocked if the gate blocks ANY of this person's tickets; signed only when every
                // required ticket is satisfied (a partially-linked registrant reads as unsigned).
                var blocked = ds.FirstOrDefault(d => d.BlockReason is not null);
                var required = ds.Any(d => d.Required);
                var signed = required ? ds.Where(d => d.Required).All(d => d.Satisfied) : ds.Any(d => d.Signed);
                var ageFacts = ds.FirstOrDefault(d => d.Birthdate is not null) ?? ds.FirstOrDefault();
                var signerFacts = ds.FirstOrDefault(d => d.Signed);
                // The rider-side ticket an ID check is recorded against (it stamps the registrant's
                // other tickets and the rider's account too), and its decision.
                var riderTicket = tickets.FirstOrDefault(r =>
                    decisionById.TryGetValue(r.PurchaseId, out var rd) && rd.RiderAudience);
                var riderFacts = riderTicket is not null ? decisionById[riderTicket.PurchaseId] : null;

                var signatureRow = tickets.FirstOrDefault(r => r.SignatureHasImage || r.HasInlineSignatureImage);
                var signedRow = tickets.FirstOrDefault(r =>
                    r.WaiverSignatureId is not null || r.WaiverSignedAt is not null || r.HasInlineSignatureImage);
                var docName = riderAudience ? riderDocName : spectatorDocName;
                var signedDocName = signedRow?.SignedWaiverName ?? signedRow?.SignedWaiverTitle;

                var first = tickets[0];
                // A ticket that names nobody is the buying account's own (e.g. the payer's line at the Gate
                // Sale): the gate's own rider name for it is the purchaser.
                var name = BuildAttendeeName(first.RiderFirstName, first.RiderLastName) ?? signedRow?.SignerName
                    ?? ds.Select(d => d.RiderName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
                var registrationComplete = tickets.All(r => r.RegistrationComplete);

                attendees.Add(new webapi.Controllers.API.Data.Redemption.OrderWaiverAttendee
                {
                    AttendeeKey = group.Key,
                    PurchaseIds = tickets.Select(r => r.PurchaseId).ToList(),
                    Name = name,
                    Audience = riderAudience ? "rider" : "spectator",
                    Birthdate = ageFacts?.Birthdate,
                    Age = ageFacts?.Age,
                    IsMinor = ageFacts?.IsMinor == true,
                    BirthdateUnknown = riderAudience && (ageFacts?.BirthdateUnknown ?? true),
                    Items = tickets.Select(r => r.TierName).ToList(),
                    RegistrationComplete = registrationComplete,
                    WaiverRequired = required,
                    // A minor's self-signed waiver does not satisfy the gate: it reads as not signed.
                    WaiverSigned = signed,
                    WaiverName = signedRow is not null ? (signedDocName ?? docName) : docName,
                    SignedAtUtc = ToUtc(signedRow?.SignatureSignedAt ?? signedRow?.WaiverSignedAt),
                    SignedByParent = signerFacts?.SignedByGuardian ?? false,
                    GuardianName = signerFacts?.GuardianName,
                    SignerName = signedRow?.SignerName,
                    SignerEmail = signedRow?.SignerEmail,
                    HasSignatureImage = signatureRow is not null,
                    SignaturePurchaseId = signatureRow?.PurchaseId,
                    BlockReason = blocked?.BlockReason,
                    // A signing link fixes a waiver problem on a ticket (not unfinished registration),
                    // addressed by default to the buyer when the buyer is not this rider.
                    CanRequestSignature = blocked is { RegistrationIncomplete: false, FixTarget: "ticket" },
                    GuardianEmail = Services.Waivers.GuardianWaiverRule.SuggestGuardianEmail(
                        first.RiderFirstName, first.RiderLastName, first.PurchaserName, first.PurchaserEmail),
                    // Yearly birthdate check, from the same decisions.
                    RecordedBirthdate = first.RiderBirthdate is { } rb ? DateOnly.FromDateTime(rb) : null,
                    IdVerifiedAtUtc = ToUtc(riderFacts?.IdVerifiedAtUtc),
                    IdVerifiedLocalDate = riderFacts?.IdVerifiedLocalDate,
                    BirthdateCheckNeeded = ds.Any(d => d.BirthdateCheckNeeded),
                    BirthdateBlocked = ds.Any(d => d.BirthdateBlocked),
                    VerifyPurchaseId = riderTicket?.PurchaseId,
                    AdultWaiverNeeded = ds.Any(d => d.AdultWaiverNeeded && d.BlockReason is not null),
                    TurnedEighteenOn = ds.FirstOrDefault(d => d.TurnedEighteenOn is not null)?.TurnedEighteenOn,
                    RiderEmail = riderFacts?.RiderEmail,
                    // Rider photo on file, from the same decisions.
                    RiderPhotoMode = riderFacts?.RiderPhotoMode ?? "off",
                    HasPhoto = riderFacts?.HasPhoto ?? false,
                    PhotoSource = riderFacts?.PhotoSource,
                    RiderPhotoId = riderFacts?.RiderPhotoId,
                    PhotoUrl = riderFacts?.PhotoUrl,
                    PhotoTakenAtUtc = ToUtc(riderFacts?.PhotoTakenAtUtc),
                    PhotoNeeded = ds.Any(d => d.PhotoNeeded),
                    PhotoBlocked = ds.Any(d => d.PhotoBlocked),
                    PhotoPurchaseId = riderTicket?.PurchaseId,
                });
            }

            // Anyone who can't walk in yet sorts to the top, then unsigned, then by name, so the
            // gate reads the problems first.
            return attendees
                .OrderByDescending(a => a.BlockReason is not null)
                .ThenByDescending(a => a.WaiverRequired && !a.WaiverSigned)
                .ThenBy(a => string.IsNullOrWhiteSpace(a.Name))
                .ThenBy(a => a.Name)
                .ToList();
        }

        private async Task<string?> ResolveWaiverName(Guid? waiverId, Guid tenantId,
            Services.Repositories.Data.PaymentData.TenantWaiver? active)
        {
            if (waiverId is null) return null;
            if (active is not null && active.Id == waiverId.Value) return WaiverLabel(active);
            var doc = await _waivers.GetById(waiverId.Value, tenantId);
            return doc is null ? null : WaiverLabel(doc);
        }

        private static string WaiverLabel(Services.Repositories.Data.PaymentData.TenantWaiver w) =>
            string.IsNullOrWhiteSpace(w.Name) ? w.Title : w.Name;

        private static string RiderLabel(Services.Repositories.Data.PaymentData.EventTicketPurchase t) =>
            $"{t.RiderFirstName} {t.RiderLastName}".Trim() is { Length: > 0 } n ? n : t.PurchaserName;

        private static DateTime? ToUtc(DateTime? value) =>
            value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;

        // Gate: staff checked this rider's photo ID and birthdate. Stamped on the rider's ticket(s)
        // in this order (the registrant's other tickets too, since the gate checks each) and, when
        // the buyer account IS the rider, on the account (per tenant), so the next visit at this
        // tenant (any event, any pass) within the tenant's window needs no re-check. The verified
        // DOB is the age evidence the waiver gate then uses, so a DOB that makes the rider a minor
        // brings the guardian rule into play on the very next lookup.
        // Gate: take (or replace) a rider's photo. Saved for the RIDER (tenant + buying account, or buyer
        // email, + rider name; RiderPhotoRules), in the image store's PRIVATE area, so their later visits
        // and events need none. Staff with sales.redeem (this controller's policy) only.
        [HttpPost("RiderPhoto")]
        [RequestSizeLimit(3 * 1024 * 1024)]
        public async Task<IActionResult> TakeRiderPhoto([FromBody] webapi.Controllers.API.Data.Redemption.TakeRiderPhotoRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var tenantId = _tenantContext.TenantId;
            if (Services.Riders.RiderPhotoRules.DecodePhoto(req.PhotoDataUrl) is not { } image)
                return new ApiResponses().BadRequestResult("The photo must be a JPEG or PNG image under 1.5 MB.");

            Services.Repositories.Data.RiderData.RiderPhoto photo;
            string riderName;
            string targetKind;
            if (req.Kind == "ticket")
            {
                var ticket = await _tickets.GetById(req.PurchaseId, tenantId);
                if (ticket is null) return new ApiResponses().NotFoundResult("Ticket not found.");
                if (ticket.Status is not ("paid" or "redeemed"))
                    return new ApiResponses().BadRequestResult($"This ticket is {ticket.Status}.");
                photo = await _riderPhotos.RecordForTicket(ticket, tenantId, image.Bytes, image.Extension, TryGetStaffUserId());
                riderName = BuildAttendeeName(ticket.RiderFirstName, ticket.RiderLastName) ?? ticket.PurchaserName;
                targetKind = "event_ticket_purchase";
            }
            else if (req.Kind == "season_pass")
            {
                var pass = await _passes.GetPurchase(req.PurchaseId);
                if (pass is null || pass.TenantId != tenantId) return new ApiResponses().NotFoundResult("Pass not found.");
                if (pass.Status != "paid") return new ApiResponses().BadRequestResult($"This pass is {pass.Status}.");
                photo = await _riderPhotos.RecordForPass(pass, tenantId, image.Bytes, image.Extension, TryGetStaffUserId());
                riderName = BuildAttendeeName(pass.HolderFirstName, pass.HolderLastName) ?? pass.PurchaserName;
                targetKind = "season_pass_purchase";
            }
            else return new ApiResponses().BadRequestResult("Unknown kind.");

            // Filed under the rider (account or buyer email + rider name), else, with nothing to key by,
            // under this credential itself (RiderPhotoRules.Key): either way it satisfies the gate here.
            await _audit.Log("rider.photo_taken", $"Rider photo taken for {riderName}",
                targetKind: targetKind, targetId: req.PurchaseId, tenantId: tenantId,
                metadata: new
                {
                    riderPhotoId = photo.Id, riderKey = photo.RiderKey,
                    owner = photo.UserId is not null ? "account" : photo.OwnerEmail is not null ? "email" : "credential",
                });
            return new ApiResponses().OkResult(new
            {
                RiderPhotoId = photo.Id,
                PhotoTakenAtUtc = ToUtc(photo.TakenAt),
            });
        }

        // A rider photo's image, for this tenant's staff only (this controller's policy). Never a public
        // URL: the image store keeps rider photos private and the bytes come through here.
        [HttpGet("RiderPhoto/{id:guid}")]
        public async Task<IActionResult> GetRiderPhoto(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (await _riderPhotos.Open(id, _tenantContext.TenantId) is not { } img)
                return new ApiResponses().NotFoundResult("Photo not found.");
            Response.Headers.CacheControl = "private, no-store";
            return File(img.Bytes, img.ContentType);
        }

        [HttpPost("Order/{token:guid}/VerifyId")]
        public async Task<IActionResult> VerifyOrderRiderId(Guid token,
            [FromBody] webapi.Controllers.API.Data.Redemption.VerifyOrderRiderIdRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var tenantId = _tenantContext.TenantId;

            // Scope: the ticket must be in the scanned token's event+purchaser set. The same rows
            // carry the tier, which decides the audience (only a rider's birthdate is checked).
            var anchor = await ResolveAnchor(token, tenantId);
            if (anchor?.EventId is null)
                return new ApiResponses().NotFoundResult("No purchase found for this token in your tenant.");
            var rows = await _tickets.ListWaiverStatusForPurchaser(
                anchor.EventId.Value, tenantId, anchor.PurchaserUserId, anchor.PurchaserEmail);
            var row = rows.FirstOrDefault(r => r.PurchaseId == req.PurchaseId);
            if (row is null)
                return new ApiResponses().NotFoundResult("That ticket isn't part of this order.");
            if (!(row.TierKind == "race_entry" || (row.TierKind == "gate_fee" && row.TierAudience == "rider")))
                return new ApiResponses().BadRequestResult("Only a rider's birthdate is checked at the gate.");

            var ticket = await _tickets.GetById(req.PurchaseId, tenantId);
            if (ticket is null) return new ApiResponses().NotFoundResult("Ticket not found.");
            if (ticket.Status is not ("paid" or "redeemed"))
                return new ApiResponses().BadRequestResult($"This ticket is {ticket.Status}, so there's nothing to verify against.");

            var (dob, dobError) = RiderIdCheckHelper.ResolveDob(req.VerifiedDob, ticket.RiderBirthdate);
            if (dobError is not null) return new ApiResponses().BadRequestResult(dobError);

            // This rider's tickets (the registrant group) stamped in one statement; the account's
            // row for this rider written once.
            var riderTicketIds = (await _tickets.ListRegistrantTickets(ticket.Id, tenantId)).Select(t => t.Id).ToList();
            var status = await _idVerification.RecordForRiderTickets(ticket, riderTicketIds, tenantId,
                TryGetStaffUserId(), dob);
            if (!status.Verified)
                return new ApiResponses().BadRequestResult(
                    "The verification did not save: the ticket changed while you were entering it. Rescan and try again.");

            var rider = BuildAttendeeName(ticket.RiderFirstName, ticket.RiderLastName) ?? ticket.PurchaserName;
            await RiderIdCheckHelper.Audit(_audit, tenantId, rider, status, dob!.Value, ticket.RiderBirthdate,
                "event_ticket_purchase", ticket.Id);

            return new ApiResponses().OkResult(new
            {
                IdVerified = true,
                IdVerifiedAtUtc = ToUtc(status.VerifiedAtUtc),
                IdVerifiedByName = status.VerifiedByName,
                IdVerifiedScope = status.Scope,
                IdVerifiedAge = status.VerifiedAge,
                DobMismatch = RiderIdCheckHelper.Differs(dob.Value, ticket.RiderBirthdate),
            });
        }

        // Gate fix-up for a waiver block (a minor who signed their own waiver, or nobody signed):
        // send the existing waiver signing link (waiver_sign_request, /SignWaiver/:token) to a
        // parent / guardian, recorded against THIS ticket or pass so the signature is linked to it
        // when signed (WaiverController.SignByToken). The request's name is the rider's, which is
        // what the signing page pre-fills as the person the waiver is for. The link comes back in
        // the response too, so staff can hand the device over when email isn't configured.
        [HttpPost("WaiverSignRequest")]
        public async Task<IActionResult> GateWaiverSignRequest(
            [FromBody] webapi.Controllers.API.Data.Redemption.GateWaiverSignRequestRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var tenantId = _tenantContext.TenantId;
            var email = req.Email?.Trim();
            if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
                return new ApiResponses().BadRequestResult("Enter the parent or guardian's email address.");

            if (req.Kind is not ("ticket" or "season_pass"))
                return new ApiResponses().BadRequestResult("Unknown kind.");

            // The gate itself says which document this rider is missing (the rider or spectator
            // document for a ticket's audience; for a pass, the event's document for an event
            // admission, else the product's) and where a signature would go.
            var target = await _waiverGate.ResolveSignTarget(tenantId,
                req.Kind == "ticket" ? req.PurchaseId : null,
                req.Kind == "season_pass" ? req.PurchaseId : null,
                req.Kind == "season_pass" ? req.EventId : null);
            if (target is null)
                return new ApiResponses().NotFoundResult(req.Kind == "ticket" ? "Ticket not found." : "Pass not found.");
            var decision = target.Decision;
            if (decision.BlockReason is null)
                return new ApiResponses().BadRequestResult($"{target.DisplayName} doesn't need a new waiver signature.");
            if (decision.RegistrationIncomplete || decision.FixTarget is null || decision.RequiredWaiverId is null)
                return new ApiResponses().BadRequestResult(
                    $"A signature won't fix this: {decision.BlockReason}");

            // An adult-waiver link goes to the RIDER, never to the buyer / guardian: their own email
            // when the gate knows it, and never the address of a buying account that isn't them.
            if (decision.AdultWaiverNeeded)
            {
                if (!string.IsNullOrWhiteSpace(decision.RiderEmail)
                    && !string.Equals(email, decision.RiderEmail.Trim(), StringComparison.OrdinalIgnoreCase))
                    return new ApiResponses().BadRequestResult(
                        $"{target.DisplayName} must sign this themselves: send the link to their own email ({decision.RiderEmail}).");
                var buyerEmail = target.TicketId is Guid tid ? (await _tickets.GetById(tid, tenantId))?.PurchaserEmail
                    : target.PassPurchaseId is Guid pid ? (await _passes.GetPurchase(pid))?.PurchaserEmail : null;
                if (string.IsNullOrWhiteSpace(decision.RiderEmail) && !string.IsNullOrWhiteSpace(buyerEmail)
                    && string.Equals(email, buyerEmail.Trim(), StringComparison.OrdinalIgnoreCase))
                    return new ApiResponses().BadRequestResult(
                        $"That is the buyer's email, not {target.DisplayName}'s. {target.DisplayName} is 18 or over and must sign this themselves: enter their own email, or hand them the device.");
            }

            var row = await _signRequests.Create(tenantId, decision.RequiredWaiverId, WaiverSignRequestMail.NewToken(),
                email, target.DisplayName, target.EventId, TryGetStaffUserId(), target.TicketId, target.PassPurchaseId);
            var sent = await WaiverSignRequestMail.TrySend(_emailer, _config, _tenantContext.Tenant, _signRequests, row);
            return new ApiResponses().OkResult(new
            {
                RequestId = row.Id,
                row.RecipientEmail,
                Sent = sent,
                Link = WaiverSignRequestMail.BuildSigningLink(_config, _tenantContext.Tenant, row.Token),
            });
        }

        // The signature image behind one ticket in the scanned order, on demand. Kept out of the
        // order payload because a drawn PNG can run to a megabyte of base64 per attendee and the
        // gate is usually on a phone. Scope: the ticket must be in the scanned token's
        // event+purchaser set, so a leaked purchase id can't pull another rider's signature.
        [HttpGet("Order/{token:guid}/Signature/{purchaseId:guid}")]
        public async Task<IActionResult> OrderSignature(Guid token, Guid purchaseId)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var tenantId = _tenantContext.TenantId;

            var anchor = await ResolveAnchor(token, tenantId);
            if (anchor?.EventId is null)
            {
                return new ApiResponses().NotFoundResult("No purchase found for this token in your tenant.");
            }

            var rows = await _tickets.ListWaiverStatusForPurchaser(
                anchor.EventId.Value, tenantId, anchor.PurchaserUserId, anchor.PurchaserEmail);
            if (!rows.Any(r => r.PurchaseId == purchaseId))
            {
                return new ApiResponses().NotFoundResult("That ticket isn't part of this order.");
            }

            var ticket = await _tickets.GetById(purchaseId, tenantId);
            if (ticket is null) return new ApiResponses().NotFoundResult("Ticket not found.");

            var sig = ticket.WaiverSignatureId.HasValue
                ? await _waivers.GetSignatureById(ticket.WaiverSignatureId.Value, tenantId)
                : null;
            var dataUrl = sig?.SignatureDataUrl ?? ticket.WaiverSignatureDataUrl;
            if (sig is null && ticket.WaiverSignedAt is null && string.IsNullOrWhiteSpace(dataUrl))
            {
                return new ApiResponses().NotFoundResult("No signature on file for this attendee.");
            }

            var docId = sig?.WaiverId ?? ticket.WaiverId;
            var doc = docId.HasValue ? await _waivers.GetById(docId.Value, tenantId) : null;
            var signedByParent = sig?.SignedByParent ?? false;

            return new ApiResponses().OkResult(new webapi.Controllers.API.Data.Redemption.OrderSignatureResponse
            {
                PurchaseId = purchaseId,
                AttendeeName = BuildAttendeeName(ticket.RiderFirstName, ticket.RiderLastName) ?? sig?.SignerName,
                WaiverName = doc?.Name,
                WaiverTitle = doc?.Title,
                SignedAtUtc = ToUtc(sig?.SignedAt ?? ticket.WaiverSignedAt),
                SignedByParent = signedByParent,
                GuardianName = signedByParent ? (sig?.ParentName ?? ticket.ParentGuardianName) : null,
                SignerName = sig?.SignerName,
                SignerEmail = sig?.SignerEmail,
                SignatureDataUrl = dataUrl,
            });
        }

        // Resolves a scanned token to the event + purchaser it belongs to (the gate scope).
        // A token can be a ticket or an add-on; an add-on with no event (counter merch) has
        // no event scope, so it's redeemed solo.
        private async Task<AnchorInfo?> ResolveAnchor(Guid token, Guid tenantId)
        {
            var tk = await _tickets.GetByRedemptionToken(token, tenantId);
            if (tk is not null)
            {
                return new AnchorInfo
                {
                    EventId = tk.EventId,
                    PurchaserUserId = tk.PurchaserUserId,
                    PurchaserEmail = tk.PurchaserEmail,
                    PurchaserName = tk.PurchaserName,
                };
            }
            var ex = await _extras.GetPurchaseByRedemptionToken(token);
            if (ex is not null && ex.TenantId == tenantId)
            {
                return new AnchorInfo
                {
                    EventId = ex.EventId,
                    PurchaserUserId = ex.PurchaserUserId,
                    PurchaserEmail = ex.PurchaserEmail,
                    PurchaserName = ex.PurchaserName,
                    SoloExtra = ex.EventId.HasValue ? null : ex,
                };
            }
            return null;
        }

        private class AnchorInfo
        {
            public Guid? EventId { get; set; }
            public Guid? PurchaserUserId { get; set; }
            public string PurchaserEmail { get; set; } = "";
            public string PurchaserName { get; set; } = "";
            // Set only when the anchor is an add-on with no event (counter merch): the
            // event+purchaser scope doesn't apply, so just this one row is redeemable.
            public Services.Repositories.Data.ExtrasData.EventExtraPurchase? SoloExtra { get; set; }
        }

        private Guid? TryGetStaffUserId()
        {
            var claim = User.FindFirst("UserId")?.Value;
            return Guid.TryParse(claim, out var id) ? id : (Guid?)null;
        }

        // The rider name captured at registration, or null when not yet registered.
        private static string? BuildAttendeeName(string? first, string? last)
        {
            var name = $"{first} {last}".Trim();
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }

        private async Task<RedemptionPreviewResponse?> LookupAsync(Guid token)
        {
            var tenantId = _tenantContext.TenantId;
            var tz = ResolveTenantTimeZone();
            var todayInTenant = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).Date;

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
                else if (!tk.RegistrationComplete)
                {
                    // Registration must be finished before check-in (the gate blocks it), so surface it
                    // here too rather than only failing at redeem time.
                    reason = "Registration not finished — rider details and waiver still needed.";
                }
                var redeemableToday = ok && tk.RegistrationComplete;

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
                    IsRedeemableToday = redeemableToday,
                    NotRedeemableReason = reason,
                    RegistrationComplete = tk.RegistrationComplete,
                    RaceNumber = tk.RaceNumber,
                    AttendeeName = BuildAttendeeName(tk.RiderFirstName, tk.RiderLastName),
                    SignedByParent = tk.SignedByParent,
                    GuardianName = tk.SignedByParent ? tk.GuardianName : null,
                };
            }

            return null;
        }

        // True when an event ticket covers more than one tenant-LOCAL calendar day: a training
        // camp, not an event that merely runs past midnight in UTC. Only event tickets qualify;
        // a day pass has no event window.
        private bool IsMultiDayEvent(RedemptionPreviewResponse preview)
        {
            if (preview.Kind != "event_ticket") return false;
            if (preview.EventStartsAtUtc is not DateTime startUtc) return false;
            if (preview.EventEndsAtUtc is not DateTime endUtc) return false;
            var tz = ResolveTenantTimeZone();
            var startLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(startUtc, DateTimeKind.Utc), tz).Date;
            var endLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(endUtc, DateTimeKind.Utc), tz).Date;
            return endLocal > startLocal;
        }

        private TimeZoneInfo ResolveTenantTimeZone()
        {
            var tz = _tenantContext.Tenant.Timezone;
            try { return TimeZoneInfo.FindSystemTimeZoneById(tz); }
            catch { return TimeZoneInfo.Utc; }
        }
    }
}
