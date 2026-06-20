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
                    var reason = ok ? null : (todayInTenant < s ? $"Event starts {s:yyyy-MM-dd}." : $"Event ended {e:yyyy-MM-dd}.");
                    resp.Items.Add(new webapi.Controllers.API.Data.Redemption.OrderItem
                    {
                        Kind = "event_ticket",
                        PurchaseId = t.Id,
                        RedemptionToken = t.RedemptionToken,
                        ItemName = $"{t.EventTitle} — {t.TierName}",
                        AmountCents = t.AmountCents,
                        Status = t.Status,
                        IsRedeemableToday = ok && t.Status == "paid",
                        NotRedeemableReason = !ok ? reason : (t.Status != "paid" ? $"Status is '{t.Status}'." : null),
                        RedeemedAtUtc = t.RedeemedAtUtc.HasValue ? DateTime.SpecifyKind(t.RedeemedAtUtc.Value, DateTimeKind.Utc) : null,
                        RegistrationComplete = t.RegistrationComplete,
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
            if (anchor.EventId.HasValue)
            {
                foreach (var t in await _tickets.ListByEventForPurchaser(anchor.EventId.Value, tenantId, anchor.PurchaserUserId, anchor.PurchaserEmail))
                    allowedTicketIds.Add(t.Id);
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
                    RegistrationComplete = tk.RegistrationComplete,
                    RaceNumber = tk.RaceNumber,
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
