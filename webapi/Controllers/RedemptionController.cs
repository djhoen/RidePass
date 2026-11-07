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
        private readonly ITenantContext _tenantContext;
        private readonly Services.Audit.IAuditLogger _audit;
        private readonly ISeasonPassRepository _passes;
        private readonly IWaiverSignRequestRepository _signRequests;
        private readonly ISmtpEmailer _emailer;
        private readonly IConfiguration _config;

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
            IConfiguration config)
        {
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
            var ticketRow = await _tickets.GetById(preview.PurchaseId, _tenantContext.TenantId);
            if (ticketRow is not null)
            {
                var waiverBlock = await _waiverGate.BlockReasonForTicket(_tenantContext.TenantId, ticketRow);
                if (waiverBlock is not null) return new ApiResponses().BadRequestResult(waiverBlock);
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
                    var block = await _waiverGate.BlockReasonForTicket(tenantId, full);
                    if (block is not null)
                    {
                        result.Outcome = "blocked";
                        result.BlockReason = block;
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
                        var waiverBlock = await _waiverGate.BlockReasonForTicket(tenantId, t);
                        if (waiverBlock is not null) { resp.Errors.Add(waiverBlock); continue; }
                        if (staffId.HasValue) await _tickets.MarkRedeemed(t.Id, tenantId, staffId.Value, nowUtc);
                        else                   await _tickets.UpdateStatus(t.Id, "redeemed");
                        resp.RedeemedCount++;
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
                        if (staffId.HasValue) await _extras.MarkRedeemed(x.Id, tenantId, staffId.Value, nowUtc);
                        else                   await _extras.UpdateStatus(x.Id, "redeemed");
                        resp.RedeemedCount++;
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

                var signatureRow = tickets.FirstOrDefault(r => r.SignatureHasImage || r.HasInlineSignatureImage);
                var signedRow = tickets.FirstOrDefault(r =>
                    r.WaiverSignatureId is not null || r.WaiverSignedAt is not null || r.HasInlineSignatureImage);
                var docName = riderAudience ? riderDocName : spectatorDocName;
                var signedDocName = signedRow?.SignedWaiverName ?? signedRow?.SignedWaiverTitle;

                var first = tickets[0];
                var name = BuildAttendeeName(first.RiderFirstName, first.RiderLastName) ?? signedRow?.SignerName;
                var registrationComplete = tickets.All(r => r.RegistrationComplete);

                attendees.Add(new webapi.Controllers.API.Data.Redemption.OrderWaiverAttendee
                {
                    AttendeeKey = group.Key,
                    PurchaseIds = tickets.Select(r => r.PurchaseId).ToList(),
                    Name = name,
                    Audience = riderAudience ? "rider" : "spectator",
                    Birthdate = ageFacts?.Birthdate is { } b
                        ? DateTime.SpecifyKind(b.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc) : null,
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

        private static DateTime? ToUtc(DateTime? value) =>
            value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;

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
