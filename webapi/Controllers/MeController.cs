using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Audit;
using Services.Helpers;
using Services.Notifications;
using Services.Payments;
using Services.Repositories.Data.CouponData;
using Services.Repositories.Interfaces;
using webapi.Controllers.API.Data.Me;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class MeController : ControllerBase
    {
        private readonly IPassPurchaseRepository _passes;
        private readonly IPassProductRepository _passProducts;
        private readonly IEventTicketPurchaseRepository _tickets;
        private readonly IEventTicketTierRepository _ticketTiers;
        private readonly ICouponRepository _coupons;
        private readonly IUpcomingPurchaseRepository _upcoming;
        private readonly IUserRepository _users;
        private readonly ITenantRepository _tenants;
        private readonly IPaymentProvider _payments;
        private readonly INotificationService _notifications;
        private readonly IAuditLogger _audit;
        private readonly Services.Waitlist.IWaitlistPromoter _waitlistPromoter;
        private readonly ISmtpEmailer _emailer;
        private readonly ILogger<MeController> _logger;
        private readonly ITenantContext _tenantContext;

        public MeController(
            IPassPurchaseRepository passes,
            IPassProductRepository passProducts,
            IEventTicketPurchaseRepository tickets,
            IEventTicketTierRepository ticketTiers,
            ICouponRepository coupons,
            IUpcomingPurchaseRepository upcoming,
            IUserRepository users,
            ITenantRepository tenants,
            IPaymentProvider payments,
            INotificationService notifications,
            IAuditLogger audit,
            Services.Waitlist.IWaitlistPromoter waitlistPromoter,
            ISmtpEmailer emailer,
            ILogger<MeController> logger,
            ITenantContext tenantContext)
        {
            _passes = passes;
            _passProducts = passProducts;
            _tickets = tickets;
            _ticketTiers = ticketTiers;
            _coupons = coupons;
            _upcoming = upcoming;
            _users = users;
            _tenants = tenants;
            _payments = payments;
            _notifications = notifications;
            _audit = audit;
            _waitlistPromoter = waitlistPromoter;
            _emailer = emailer;
            _logger = logger;
            _tenantContext = tenantContext;
        }

        /// <summary>
        /// CROSS-TENANT feed of paid purchases the signed-in rider still has
        /// coming up: future event tickets, day passes for today or later,
        /// valid season passes, and valid memberships. Intended for the apex
        /// landing page (ridepass.io/User/Upcoming). No tenant context check
        /// because this endpoint deliberately spans every tenant the rider
        /// has bought from; scope is the rider's identity in the JWT.
        /// </summary>
        [HttpGet("Upcoming")]
        public async Task<IActionResult> GetUpcoming()
        {
            if (!Guid.TryParse(User.FindFirst("UserId")?.Value, out var userId))
            {
                return new ApiResponses().BadRequestResult("Invalid token.");
            }

            var rows = await _upcoming.ListForUser(userId);
            var items = rows.Select(r => new UpcomingItemResponse
            {
                Kind = r.Kind,
                Id = r.Id,
                TenantId = r.TenantId,
                TenantSubdomain = r.TenantSubdomain,
                TenantDisplayName = r.TenantDisplayName,
                ItemName = r.ItemName,
                OccursAtUtc = r.OccursAtUtc.HasValue
                    ? DateTime.SpecifyKind(r.OccursAtUtc.Value, DateTimeKind.Utc) : null,
                ValidToUtc = r.ValidToUtc.HasValue
                    ? DateTime.SpecifyKind(r.ValidToUtc.Value, DateTimeKind.Utc) : null,
                AmountCents = r.AmountCents,
                RedemptionToken = r.RedemptionToken,
                CreatedAtUtc = DateTime.SpecifyKind(r.CreatedAtUtc, DateTimeKind.Utc),
            }).ToList();

            return new ApiResponses().OkResult(items);
        }

        [HttpGet("Purchases")]
        public async Task<IActionResult> GetMyPurchases()
        {
            if (!_tenantContext.IsResolved)
            {
                return new ApiResponses().BadRequestResult("No tenant resolved.");
            }

            var userIdClaim = User.FindFirst("UserId")?.Value;
            if (!Guid.TryParse(userIdClaim, out var userId))
            {
                return new ApiResponses().BadRequestResult("Invalid token.");
            }

            var passes = await _passes.GetForUser(userId, _tenantContext.TenantId);
            var tickets = await _tickets.GetForUser(userId, _tenantContext.TenantId);

            var combined = new List<MyPurchaseResponse>();
            combined.AddRange(passes.Select(dp => new MyPurchaseResponse
            {
                Kind = "pass",
                Id = dp.Id,
                ItemName = dp.ProductName,
                ValidOnDate = dp.ValidOnDate,
                AmountCents = dp.AmountCents,
                Status = dp.Status,
                RedemptionToken = dp.RedemptionToken,
                CreatedAtUtc = DateTime.SpecifyKind(dp.CreatedAt, DateTimeKind.Utc),
            }));
            combined.AddRange(tickets.Select(tk => new MyPurchaseResponse
            {
                Kind = "event_ticket",
                Id = tk.Id,
                ItemName = $"{tk.EventTitle} — {tk.TierName}",
                EventId = tk.EventId,
                EventStartsAtUtc = DateTime.SpecifyKind(tk.EventStartsAt, DateTimeKind.Utc),
                AmountCents = tk.AmountCents,
                Status = tk.Status,
                RedemptionToken = tk.RedemptionToken,
                CreatedAtUtc = DateTime.SpecifyKind(tk.CreatedAt, DateTimeKind.Utc),
                TierKind = tk.TierKind,
            }));

            var ordered = combined.OrderByDescending(p => p.CreatedAtUtc).ToList();
            return new ApiResponses().OkResult(ordered);
        }

        /// <summary>
        /// Coupons issued to me at this tenant — populated by Phase-2 race-entry bundles.
        /// Includes the source purchase id so the rider UI can group coupons under the
        /// race-entry ticket they came from. Filter by ?ticketPurchaseId=X to fetch one
        /// purchase's bundle directly.
        /// </summary>
        [HttpGet("Coupons")]
        public async Task<IActionResult> GetMyCoupons([FromQuery] Guid? ticketPurchaseId)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!Guid.TryParse(User.FindFirst("UserId")?.Value, out var userId))
                return new ApiResponses().BadRequestResult("Invalid token.");

            var coupons = ticketPurchaseId.HasValue
                ? (await _coupons.ListIssuedFromPurchase(ticketPurchaseId.Value))
                    .Where(c => c.TenantId == _tenantContext.TenantId && c.IssuedToUserId == userId)
                    .ToList()
                : await _coupons.ListIssuedToUser(userId, _tenantContext.TenantId);

            var responses = new List<MyCouponResponse>(coupons.Count);
            foreach (var c in coupons)
            {
                var redemptions = await _coupons.CountRedemptions(c.Id);
                var shares = await _coupons.ListSharesByCoupon(c.Id);
                var latest = shares.Count > 0 ? shares[0] : null;
                responses.Add(new MyCouponResponse
                {
                    Id = c.Id,
                    Code = c.Code,
                    Description = c.Description,
                    DiscountKind = c.DiscountKind,
                    DiscountValue = c.DiscountValue,
                    ApplicableScope = c.ApplicableScope,
                    ValidToUtc = c.ValidToUtc,
                    IssuedFromPurchaseId = c.IssuedFromPurchaseId,
                    IsActive = c.IsActive,
                    RedeemedCount = redemptions,
                    MaxTotalUses = c.MaxTotalUses,
                    ShareCount = shares.Count,
                    LastSharedAtUtc = latest?.SentAt,
                    LastSharedToEmail = latest?.RecipientEmail,
                });
            }
            return new ApiResponses().OkResult(responses);
        }

        /// <summary>
        /// Rider shares one of their issued coupons with a friend by email. Records the
        /// share row (marketing capture) and best-effort emails the recipient with the
        /// code. Allows multiple shares per coupon (in case the friend lost the email),
        /// but blocks once the coupon has been redeemed.
        /// </summary>
        [HttpPost("Coupons/{couponId:guid}/Share")]
        public async Task<IActionResult> ShareCoupon(Guid couponId, [FromBody] ShareCouponRequest request)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!Guid.TryParse(User.FindFirst("UserId")?.Value, out var userId))
                return new ApiResponses().BadRequestResult("Invalid token.");
            if (string.IsNullOrWhiteSpace(request.RecipientEmail))
                return new ApiResponses().BadRequestResult("Recipient email is required.");

            var coupon = await _coupons.GetById(couponId, _tenantContext.TenantId);
            if (coupon is null) return new ApiResponses().NotFoundResult("Coupon not found.");
            if (coupon.IssuedToUserId != userId)
                return new ApiResponses().BadRequestResult("You can only share coupons that were issued to you.");

            // Block sharing a coupon that's already been redeemed — there's no benefit
            // to sending a single-use code that can't be redeemed.
            var redemptionCount = await _coupons.CountRedemptions(couponId);
            if (coupon.MaxTotalUses.HasValue && redemptionCount >= coupon.MaxTotalUses.Value)
                return new ApiResponses().BadRequestResult("This coupon has already been used and can't be re-shared.");

            var share = new CouponShare
            {
                CouponId = couponId,
                TenantId = _tenantContext.TenantId,
                SenderUserId = userId,
                RecipientEmail = request.RecipientEmail.Trim(),
                RecipientName = string.IsNullOrWhiteSpace(request.RecipientName) ? null : request.RecipientName.Trim(),
                PersonalNote = string.IsNullOrWhiteSpace(request.PersonalNote) ? null : request.PersonalNote.Trim(),
            };
            share.Id = await _coupons.RecordShare(share);

            // Email send is fire-and-forget so a slow SMTP server doesn't block the
            // rider's UI. The share row is already saved; if email fails we log it.
            _ = SendShareEmailAsync(coupon, share);

            return new ApiResponses().OkResult(new { sharedAt = share.SentAt });
        }

        // ── Rider self-cancel / cancel-request ────────────────────────────────
        // Two paths driven by tenant.AllowSelfCancel:
        //   - allowed   → cancel + partial refund inline (rider portion of the
        //                  service charge is withheld per RefundCalculator).
        //   - disallowed → no DB change to the purchase; we emit a notification
        //                  to tenant admins so they can process the cancel manually
        //                  via the existing admin Cancel button.
        // Both paths return the same shape so the frontend can use one handler.

        [HttpPost("Purchases/Pass/{id:guid}/Cancel")]
        public async Task<IActionResult> CancelMyPass(Guid id, [FromBody] CancelMyPurchaseRequest req, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!Guid.TryParse(User.FindFirst("UserId")?.Value, out var userId))
                return new ApiResponses().BadRequestResult("Invalid token.");

            var purchase = await _passes.GetById(id, _tenantContext.TenantId);
            if (purchase is null || purchase.PurchaserUserId != userId)
                return new ApiResponses().NotFoundResult("Purchase not found.");
            if (purchase.Status != "paid")
                return new ApiResponses().BadRequestResult($"Cannot cancel a purchase with status '{purchase.Status}'.");

            var tenant = _tenantContext.Tenant;
            var reason = string.IsNullOrWhiteSpace(req.Reason) ? null : req.Reason.Trim();

            if (!tenant.AllowSelfCancel)
            {
                await EmitCancelRequest(tenant.Id, "pass", purchase.Id, purchase.PurchaserName,
                    purchase.PurchaserEmail, purchase.AmountCents, reason);
                return new ApiResponses().OkResult(new { id, status = "request_submitted" });
            }

            var product = await _passProducts.GetById(purchase.ProductId, _tenantContext.TenantId);
            var refundCents = RefundCalculator.RefundableCents(
                purchase.AmountCents, purchase.ServiceChargeCents, product?.RiderPaidServiceChargeBps ?? 10000);

            await _passes.Cancel(id, _tenantContext.TenantId, userId, reason);
            string? refundId = null;
            if (refundCents > 0 && !string.IsNullOrEmpty(purchase.StripePaymentIntentId))
            {
                try
                {
                    var refund = await _payments.RefundAsync(purchase.StripePaymentIntentId!, refundCents,
                        idempotencyKey: $"refund-pass-{id}-{refundCents}", ct: ct);
                    refundId = refund.RefundId;
                    await _passes.MarkRefunded(id, $"stripe_refund={refundId} status={refund.Status} amount_cents={refundCents}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Stripe refund failed during self-cancel of pass {Id}", id);
                    // Still leave the row cancelled — admins can retry the refund manually.
                }
            }
            await _audit.Log("rider.self_cancel", $"Rider cancelled pass — refund ${refundCents/100m:0.00}",
                "pass_purchase", id, _tenantContext.TenantId, new { reason, refundCents, refundId });
            if (purchase.EventId.HasValue)
            {
                _ = _waitlistPromoter.PromoteNext(purchase.EventId.Value, null);
            }
            return new ApiResponses().OkResult(new { id, status = "cancelled", refundCents, refundId });
        }

        [HttpPost("Purchases/Ticket/{id:guid}/Cancel")]
        public async Task<IActionResult> CancelMyTicket(Guid id, [FromBody] CancelMyPurchaseRequest req, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!Guid.TryParse(User.FindFirst("UserId")?.Value, out var userId))
                return new ApiResponses().BadRequestResult("Invalid token.");

            var purchase = await _tickets.GetById(id, _tenantContext.TenantId);
            if (purchase is null || purchase.PurchaserUserId != userId)
                return new ApiResponses().NotFoundResult("Ticket not found.");
            if (purchase.Status != "paid")
                return new ApiResponses().BadRequestResult($"Cannot cancel a ticket with status '{purchase.Status}'.");

            var tenant = _tenantContext.Tenant;
            var reason = string.IsNullOrWhiteSpace(req.Reason) ? null : req.Reason.Trim();

            if (!tenant.AllowSelfCancel)
            {
                await EmitCancelRequest(tenant.Id, "event_ticket", purchase.Id, purchase.PurchaserName,
                    purchase.PurchaserEmail, purchase.AmountCents, reason);
                return new ApiResponses().OkResult(new { id, status = "request_submitted" });
            }

            var tier = await _ticketTiers.GetById(purchase.TierId, _tenantContext.TenantId);
            var refundCents = RefundCalculator.RefundableCents(
                purchase.AmountCents, purchase.ServiceChargeCents, tier?.RiderPaidServiceChargeBps ?? 10000);

            await _tickets.Cancel(id, _tenantContext.TenantId, userId, reason);
            string? refundId = null;
            if (refundCents > 0 && !string.IsNullOrEmpty(purchase.StripePaymentIntentId))
            {
                try
                {
                    var refund = await _payments.RefundAsync(purchase.StripePaymentIntentId!, refundCents,
                        idempotencyKey: $"refund-ticket-{id}-{refundCents}", ct: ct);
                    refundId = refund.RefundId;
                    await _tickets.MarkRefunded(id, $"stripe_refund={refundId} status={refund.Status} amount_cents={refundCents}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Stripe refund failed during self-cancel of ticket {Id}", id);
                }
            }
            await _audit.Log("rider.self_cancel", $"Rider cancelled ticket — refund ${refundCents/100m:0.00}",
                "event_ticket_purchase", id, _tenantContext.TenantId, new { reason, refundCents, refundId });
            if (tier is not null)
            {
                _ = _waitlistPromoter.PromoteNext(tier.EventId, purchase.TierId);
            }
            return new ApiResponses().OkResult(new { id, status = "cancelled", refundCents, refundId });
        }

        private async Task EmitCancelRequest(Guid tenantId, string sourceKind, Guid sourceId,
            string riderName, string riderEmail, int amountCents, string? reason)
        {
            var label = sourceKind == "pass" ? "pass" : "ticket";
            var body = $"{riderName} ({riderEmail}) requested to cancel a {label} for ${amountCents/100m:0.00}." +
                       (string.IsNullOrEmpty(reason) ? string.Empty : $"\nReason: {reason}");
            await _notifications.EmitToTenantAdmins(tenantId, "cancel_request",
                $"Cancel request: {label}",
                body,
                "/Admin/Purchases");
            await _audit.Log("rider.cancel_request", $"Rider requested cancellation of {label}",
                $"{sourceKind}_purchase", sourceId, tenantId, new { reason, amountCents });
        }

        private async Task SendShareEmailAsync(Coupon coupon, CouponShare share)
        {
            if (!_emailer.IsConfigured) return;
            try
            {
                var tenant = await _tenants.GetById(share.TenantId);
                if (tenant is null) return;
                var sender = share.SenderUserId.HasValue ? await _users.GetById(share.SenderUserId.Value) : null;
                var senderName = sender is null ? "A friend" : $"{sender.FirstName} {sender.LastName}".Trim();

                var discountStr = coupon.DiscountKind == "percent"
                    ? $"{coupon.DiscountValue / 100}% off"
                    : $"${coupon.DiscountValue / 100m:0.00} off";

                var note = string.IsNullOrEmpty(share.PersonalNote)
                    ? string.Empty
                    : $"<blockquote style=\"border-left:3px solid #ccc;padding-left:1em;color:#555\">{System.Net.WebUtility.HtmlEncode(share.PersonalNote)}</blockquote>";

                var subject = $"{senderName} sent you a {tenant.DisplayName} coupon";
                var html = $@"<p>Hi{(string.IsNullOrEmpty(share.RecipientName) ? "" : $" {System.Net.WebUtility.HtmlEncode(share.RecipientName)}")},</p>
<p><strong>{System.Net.WebUtility.HtmlEncode(senderName)}</strong> sent you a coupon for <strong>{System.Net.WebUtility.HtmlEncode(tenant.DisplayName)}</strong>.</p>
{note}
<p>Use this code at checkout to get <strong>{discountStr}</strong>:</p>
<p style=""font-family:monospace;font-size:1.4em;font-weight:bold;padding:8px 14px;border:1px solid #ddd;display:inline-block"">
  {System.Net.WebUtility.HtmlEncode(coupon.Code)}
</p>
{(coupon.ValidToUtc.HasValue ? $"<p style=\"color:#888\">Expires {coupon.ValidToUtc.Value:MMMM d, yyyy}.</p>" : "")}";

                await _emailer.Send(share.RecipientEmail, subject, html);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send coupon-share email for coupon {CouponId} to {Recipient}",
                    coupon.Id, share.RecipientEmail);
            }
        }
    }
}
