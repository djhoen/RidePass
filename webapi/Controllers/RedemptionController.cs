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
    [Route("api/[controller]")]
    [Authorize(Policy = TenantPermissions.Policy.SalesRedeem)]
    public class RedemptionController : ControllerBase
    {
        private readonly IEventTicketPurchaseRepository _tickets;
        private readonly IEventExtraRepository _extras;
        private readonly IUserRepository _users;
        private readonly Services.Waivers.IWaiverCheckInGate _waiverGate;
        private readonly ITenantContext _tenantContext;

        public RedemptionController(
            IEventTicketPurchaseRepository tickets,
            IEventExtraRepository extras,
            IUserRepository users,
            Services.Waivers.IWaiverCheckInGate waiverGate,
            ITenantContext tenantContext)
        {
            _tickets = tickets;
            _extras = extras;
            _users = users;
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
            if (staffId.HasValue) await _tickets.MarkRedeemed(preview.PurchaseId, tenantId, staffId.Value, nowUtc);
            else                 await _tickets.UpdateStatus(preview.PurchaseId, "redeemed");

            preview.Status = "redeemed";
            return new ApiResponses().OkResult(preview);
        }

        // Event-wide check-in roster: every paid/redeemed attendee for one event with the
        // attributes the operator app filters on (race class / gate fee, rider vs spectator,
        // checked-in state, race number). Powers the live roster view and the offline roster
        // snapshot. Event- and tenant-scoped; SalesRedeem (class-level) gates it.
        [HttpGet("Roster/{eventId:guid}")]
        public async Task<IActionResult> Roster(Guid eventId)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var rows = await _tickets.ListEventRoster(eventId, _tenantContext.TenantId);
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
                if (row.Status == "redeemed")
                {
                    // Already in: your own re-sync is idempotent success; another device that
                    // got there first is a conflict to surface.
                    result.RedeemedByUserId = row.RedeemedByUserId;
                    result.RedeemedAtUtc = row.RedeemedAtUtc;
                    result.Outcome = row.RedeemedByUserId == staffId.Value ? "admitted" : "conflict";
                    results.Add(result);
                    continue;
                }
                if (row.Status != "paid")
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
                foreach (var t in ticketRows)
                {
                    var s = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(t.EventStartsAt, DateTimeKind.Utc), tz).Date;
                    var e = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(t.EventEndsAt, DateTimeKind.Utc), tz).Date;
                    var ok = todayInTenant >= s && todayInTenant <= e;
                    var windowReason = ok ? null : (todayInTenant < s ? $"Event starts {s:yyyy-MM-dd}." : $"Event ended {e:yyyy-MM-dd}.");
                    // A rider whose registration isn't finished can't be checked in (the gate blocks it),
                    // so mark it non-redeemable with a clear reason instead of letting staff select it and
                    // hit a redeem error. Priority: window → status → registration.
                    var redeemable = ok && t.Status == "paid" && t.RegistrationComplete;
                    var reason = !ok ? windowReason
                        : t.Status != "paid" ? $"Status is '{t.Status}'."
                        : !t.RegistrationComplete ? "Registration not finished — rider details and waiver still needed."
                        : null;
                    resp.Items.Add(new webapi.Controllers.API.Data.Redemption.OrderItem
                    {
                        Kind = "event_ticket",
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
            }
            else if (anchor.SoloExtra is not null)
            {
                // No-event add-on (counter merch): only the scanned row is in scope.
                resp.Items.Add(BuildExtraItem(anchor.SoloExtra));
                if (anchor.SoloExtra.RedeemedByUserId.HasValue) redeemerIds.Add(anchor.SoloExtra.RedeemedByUserId.Value);
            }

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

            return new ApiResponses().OkResult(resp);
        }

        private static webapi.Controllers.API.Data.Redemption.OrderItem BuildExtraItem(
            Services.Repositories.Data.ExtrasData.EventExtraPurchase x) =>
            new webapi.Controllers.API.Data.Redemption.OrderItem
            {
                Kind = "extras",
                PurchaseId = x.Id,
                RedemptionToken = x.RedemptionToken,
                ItemName = "Add-on",
                AmountCents = x.AmountCents,
                Status = x.Status,
                IsRedeemableToday = x.Status == "paid",
                NotRedeemableReason = x.Status != "paid" ? $"Status is '{x.Status}'." : null,
                RedeemedAtUtc = x.RedeemedAtUtc.HasValue ? DateTime.SpecifyKind(x.RedeemedAtUtc.Value, DateTimeKind.Utc) : null,
            };

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

        private TimeZoneInfo ResolveTenantTimeZone()
        {
            var tz = _tenantContext.Tenant.Timezone;
            try { return TimeZoneInfo.FindSystemTimeZoneById(tz); }
            catch { return TimeZoneInfo.Utc; }
        }
    }
}
