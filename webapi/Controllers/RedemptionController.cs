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
        private readonly ITenantContext _tenantContext;

        public RedemptionController(
            IEventTicketPurchaseRepository tickets,
            IEventExtraRepository extras,
            IUserRepository users,
            ITenantContext tenantContext)
        {
            _tickets = tickets;
            _extras = extras;
            _users = users;
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

            var staffId = TryGetStaffUserId();
            var nowUtc = DateTime.UtcNow;
            var tenantId = _tenantContext.TenantId;
            if (staffId.HasValue) await _tickets.MarkRedeemed(preview.PurchaseId, tenantId, staffId.Value, nowUtc);
            else                 await _tickets.UpdateStatus(preview.PurchaseId, "redeemed");

            preview.Status = "redeemed";
            return new ApiResponses().OkResult(preview);
        }

        // Scan-once-redeem-many: given any token from a customer's order, surface
        // every other purchase row tied to the same Stripe PaymentIntent so the
        // gate worker can pick which items (gate fee, t-shirt, parking, ...) to
        // redeem now and which to leave for later.
        [HttpGet("Order/{token:guid}")]
        public async Task<IActionResult> Order(Guid token)
        {
            var tenantId = _tenantContext.TenantId;
            var anchorPi = await ResolveAnchorPaymentIntentId(token, tenantId);
            if (anchorPi.AnchorRow is null)
            {
                return new ApiResponses().NotFoundResult("No purchase found for this token in your tenant.");
            }

            var resp = new webapi.Controllers.API.Data.Redemption.OrderLookupResponse
            {
                StripePaymentIntentId = anchorPi.PaymentIntentId is null ? null : Guid.Empty, // placeholder, see below
                PurchaserName = anchorPi.PurchaserName,
                PurchaserEmail = anchorPi.PurchaserEmail,
            };
            resp.StripePaymentIntentId = null; // we'll just leave the FE to ignore it; PI is a string and not relevant here.

            // Pre-resolve the staff name lookup for redeemed_by ids surfaced below.
            var redeemerIds = new HashSet<Guid>();

            var tz = ResolveTenantTimeZone();
            var todayInTenant = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).Date;

            // Purchases on this PI.
            if (anchorPi.PaymentIntentId is not null)
            {
                var ticketRows = await _tickets.ListByStripePaymentIntentId(anchorPi.PaymentIntentId);
                foreach (var t in ticketRows.Where(t => t.TenantId == tenantId))
                {
                    var ctx = await _tickets.GetByRedemptionToken(t.RedemptionToken, tenantId);
                    var startUtc = ctx is null ? (DateTime?)null : DateTime.SpecifyKind(ctx.EventStartsAt, DateTimeKind.Utc);
                    var endUtc = ctx is null ? (DateTime?)null : DateTime.SpecifyKind(ctx.EventEndsAt, DateTimeKind.Utc);
                    bool ok = true; string? reason = null;
                    if (startUtc.HasValue && endUtc.HasValue)
                    {
                        var s = TimeZoneInfo.ConvertTimeFromUtc(startUtc.Value, tz).Date;
                        var e = TimeZoneInfo.ConvertTimeFromUtc(endUtc.Value, tz).Date;
                        ok = todayInTenant >= s && todayInTenant <= e;
                        if (!ok) reason = todayInTenant < s ? $"Event starts {s:yyyy-MM-dd}." : $"Event ended {e:yyyy-MM-dd}.";
                    }
                    var name = ctx is not null ? $"{ctx.EventTitle} — {ctx.TierName}" : "Race Entry";
                    var item = new webapi.Controllers.API.Data.Redemption.OrderItem
                    {
                        Kind = "event_ticket",
                        PurchaseId = t.Id,
                        RedemptionToken = t.RedemptionToken,
                        ItemName = name,
                        AmountCents = t.AmountCents,
                        Status = t.Status,
                        IsRedeemableToday = ok && t.Status == "paid",
                        NotRedeemableReason = !ok ? reason : (t.Status != "paid" ? $"Status is '{t.Status}'." : null),
                        RedeemedAtUtc = t.RedeemedAtUtc.HasValue ? DateTime.SpecifyKind(t.RedeemedAtUtc.Value, DateTimeKind.Utc) : null,
                        RegistrationComplete = t.RegistrationComplete,
                    };
                    if (t.RedeemedByUserId.HasValue) redeemerIds.Add(t.RedeemedByUserId.Value);
                    resp.Items.Add(item);
                }

                var extraRows = await _extras.ListByPaymentIntentId(anchorPi.PaymentIntentId);
                foreach (var x in extraRows.Where(x => x.TenantId == tenantId))
                {
                    var item = new webapi.Controllers.API.Data.Redemption.OrderItem
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
                    if (x.RedeemedByUserId.HasValue) redeemerIds.Add(x.RedeemedByUserId.Value);
                    resp.Items.Add(item);
                }
            }
            else
            {
                // Cash counter sale (no PI). Fall back to the single anchor row.
                resp.Items.Add(anchorPi.AnchorItem!);
                if (anchorPi.AnchorItem!.RedeemedAtUtc.HasValue) { /* nothing extra */ }
            }

            // Resolve redeemer names in one shot.
            foreach (var id in redeemerIds)
            {
                var u = await _users.GetById(id);
                if (u is null) continue;
                var name = $"{u.FirstName} {u.LastName}".Trim();
                foreach (var item in resp.Items)
                {
                    if ((item.Kind == "event_ticket" || item.Kind == "extras")
                        && item.RedeemedAtUtc.HasValue && item.RedeemedByName is null)
                    {
                        // We don't have the user-id on the response right here; fill via a second pass.
                    }
                }
            }
            // Second pass: re-query rows we already loaded to fill RedeemedByName. To keep
            // the code simple and avoid a third query each, walk the response items + look
            // up each redeemed-by name via the same id set built above.
            var staffById = new Dictionary<Guid, string>();
            foreach (var id in redeemerIds)
            {
                var u = await _users.GetById(id);
                if (u is null) continue;
                staffById[id] = $"{u.FirstName} {u.LastName}".Trim();
            }
            // Stamp names on items by matching back through the originating rows.
            // (We re-fetch anchor row by id where needed — small N.)
            foreach (var item in resp.Items)
            {
                if (!item.RedeemedAtUtc.HasValue) continue;
                Guid? byId = item.Kind switch
                {
                    "event_ticket" => (await _tickets.GetById(item.PurchaseId, tenantId))?.RedeemedByUserId,
                    "extras" => (await _extras.GetPurchase(item.PurchaseId))?.RedeemedByUserId,
                    _ => null,
                };
                if (byId.HasValue && staffById.TryGetValue(byId.Value, out var nm))
                {
                    item.RedeemedByName = nm;
                }
            }

            return new ApiResponses().OkResult(resp);
        }

        [HttpPost("Order/Redeem")]
        public async Task<IActionResult> RedeemBulk([FromBody] webapi.Controllers.API.Data.Redemption.BulkRedeemRequest req)
        {
            var tenantId = _tenantContext.TenantId;
            // Authorization scope: every requested item must live under the SAME PI as the
            // scanned anchor token so a leaked purchase id can't redeem someone else's pass.
            var anchor = await ResolveAnchorPaymentIntentId(req.OrderToken, tenantId);
            if (anchor.AnchorRow is null)
            {
                return new ApiResponses().NotFoundResult("Order not found.");
            }
            var allowedPi = anchor.PaymentIntentId;

            var staffId = TryGetStaffUserId();
            var nowUtc = DateTime.UtcNow;
            var resp = new webapi.Controllers.API.Data.Redemption.BulkRedeemResponse();

            foreach (var entry in req.Items.DistinctBy(i => (i.Kind, i.PurchaseId)))
            {
                try
                {
                    if (entry.Kind == "event_ticket")
                    {
                        var t = await _tickets.GetById(entry.PurchaseId, tenantId);
                        if (t is null) { resp.Errors.Add($"Ticket {entry.PurchaseId} not found."); continue; }
                        if (allowedPi is not null && t.StripePaymentIntentId != allowedPi)
                        {
                            resp.Errors.Add($"Ticket {entry.PurchaseId} doesn't belong to this order."); continue;
                        }
                        if (t.Status == "redeemed") { resp.Errors.Add("A ticket was already redeemed — skipped."); continue; }
                        if (t.Status != "paid") { resp.Errors.Add($"Ticket status is '{t.Status}' — can't redeem."); continue; }
                        if (staffId.HasValue) await _tickets.MarkRedeemed(t.Id, tenantId, staffId.Value, nowUtc);
                        else                   await _tickets.UpdateStatus(t.Id, "redeemed");
                        resp.RedeemedCount++;
                    }
                    else if (entry.Kind == "extras")
                    {
                        var x = await _extras.GetPurchase(entry.PurchaseId);
                        if (x is null || x.TenantId != tenantId) { resp.Errors.Add($"Add-on {entry.PurchaseId} not found."); continue; }
                        if (allowedPi is not null && x.StripePaymentIntentId != allowedPi)
                        {
                            resp.Errors.Add($"Add-on {entry.PurchaseId} doesn't belong to this order."); continue;
                        }
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

        // Resolves the scanned token to (a) the anchor row + (b) the PI id used to
        // group all sibling rows on the order. Cash counter sales have no PI, so
        // bulk-redeem there falls back to single-item.
        private async Task<AnchorLookup> ResolveAnchorPaymentIntentId(Guid token, Guid tenantId)
        {
            var tk = await _tickets.GetByRedemptionToken(token, tenantId);
            if (tk is not null)
            {
                return new AnchorLookup
                {
                    AnchorRow = tk,
                    PaymentIntentId = tk.StripePaymentIntentId,
                    PurchaserName = tk.PurchaserName,
                    PurchaserEmail = tk.PurchaserEmail,
                    AnchorItem = new webapi.Controllers.API.Data.Redemption.OrderItem
                    {
                        Kind = "event_ticket",
                        PurchaseId = tk.Id,
                        RedemptionToken = tk.RedemptionToken,
                        ItemName = $"{tk.EventTitle} — {tk.TierName}",
                        AmountCents = tk.AmountCents,
                        Status = tk.Status,
                        IsRedeemableToday = tk.Status == "paid",
                        NotRedeemableReason = tk.Status != "paid" ? $"Status is '{tk.Status}'." : null,
                        RegistrationComplete = tk.RegistrationComplete,
                    },
                };
            }
            var ex = await _extras.GetPurchaseByRedemptionToken(token);
            if (ex is not null && ex.TenantId == tenantId)
            {
                return new AnchorLookup
                {
                    AnchorRow = ex,
                    PaymentIntentId = ex.StripePaymentIntentId,
                    PurchaserName = ex.PurchaserName,
                    PurchaserEmail = ex.PurchaserEmail,
                    AnchorItem = new webapi.Controllers.API.Data.Redemption.OrderItem
                    {
                        Kind = "extras",
                        PurchaseId = ex.Id,
                        RedemptionToken = ex.RedemptionToken,
                        ItemName = "Add-on",
                        AmountCents = ex.AmountCents,
                        Status = ex.Status,
                        IsRedeemableToday = ex.Status == "paid",
                        NotRedeemableReason = ex.Status != "paid" ? $"Status is '{ex.Status}'." : null,
                    },
                };
            }
            return new AnchorLookup();
        }

        private class AnchorLookup
        {
            public object? AnchorRow { get; set; }
            public string? PaymentIntentId { get; set; }
            public string PurchaserName { get; set; } = "";
            public string PurchaserEmail { get; set; } = "";
            public webapi.Controllers.API.Data.Redemption.OrderItem? AnchorItem { get; set; }
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
