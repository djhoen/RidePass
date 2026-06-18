using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Payments;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Data.WaitlistData;
using Services.Repositories.Interfaces;
using webapi.Controllers.API.Data.Waitlist;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class WaitlistController : ControllerBase
    {
        private readonly IEventWaitlistRepository _waitlist;
        private readonly IEventRepository _events;
        private readonly IEventTicketTierRepository _tiers;
        private readonly IEventTicketPurchaseRepository _ticketPurchases;
        private readonly IUserRepository _users;
        private readonly IPaymentProvider _payments;
        private readonly ITenantContext _tenantContext;

        public WaitlistController(
            IEventWaitlistRepository waitlist,
            IEventRepository events,
            IEventTicketTierRepository tiers,
            IEventTicketPurchaseRepository ticketPurchases,
            IUserRepository users,
            IPaymentProvider payments,
            ITenantContext tenantContext)
        {
            _waitlist = waitlist;
            _events = events;
            _tiers = tiers;
            _ticketPurchases = ticketPurchases;
            _users = users;
            _payments = payments;
            _tenantContext = tenantContext;
        }

        [HttpPost("Join")]
        public async Task<IActionResult> Join([FromBody] JoinWaitlistRequest req, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!_tenantContext.Tenant.WaitlistEnabled)
                return new ApiResponses().BadRequestResult("Waitlists aren't enabled at this track.");
            if (!Guid.TryParse(User.FindFirst("UserId")?.Value, out var userId))
                return new ApiResponses().BadRequestResult("Invalid token.");

            var user = await _users.GetById(userId);
            if (user is null) return new ApiResponses().BadRequestResult("User not found.");
            if (string.IsNullOrWhiteSpace(user.Phone))
            {
                return new ApiResponses().BadRequestResult(
                    "Add a mobile phone to your profile before joining a waitlist — we text you when a spot opens.");
            }

            var ev = await _events.GetById(req.EventId, _tenantContext.TenantId);
            if (ev is null || ev.Status != "scheduled")
                return new ApiResponses().BadRequestResult("Event not available.");
            if (ev.EndsAt < DateTime.UtcNow)
                return new ApiResponses().BadRequestResult("Event has already ended.");

            // Make sure the rider isn't already in this bucket.
            var existing = await _waitlist.GetActiveForUser(req.EventId, req.TierId, userId);
            if (existing is not null)
            {
                return new ApiResponses().BadRequestResult("You're already on this waitlist.");
            }

            // Waitlists are per-tier (race class or gate fee) — the rider waitlists for the
            // exact admission that was full, and a promotion later charges that same tier.
            int prepayAmountCents = 0;
            if (!req.TierId.HasValue)
                return new ApiResponses().BadRequestResult("Pick an admission to join its waitlist.");
            var tier = await _tiers.GetById(req.TierId.Value, _tenantContext.TenantId);
            if (tier is null || tier.EventId != req.EventId || !tier.IsActive)
                return new ApiResponses().BadRequestResult("Selected admission isn't available.");
            if (!tier.Inventory.HasValue)
                return new ApiResponses().BadRequestResult("This admission has unlimited capacity — no waitlist needed.");
            var sold = await _tiers.SoldCount(tier.Id);
            if (sold < tier.Inventory.Value)
                return new ApiResponses().BadRequestResult("Spots are still available — buy directly instead.");

            if (req.Prepay)
            {
                // Compute pre-pay amount the same way regular checkout does so the rider
                // ends up with no surprise charge when promoted. ServiceCharge is the
                // tenant fee; rider pays their share per tier.RiderPaidServiceChargeBps.
                var serviceChargePerUnit = (int)((long)tier.PriceCents * _tenantContext.Tenant.ServiceChargeBps / 10_000L);
                var riderPortion = (int)((long)serviceChargePerUnit * tier.RiderPaidServiceChargeBps / 10_000L);
                prepayAmountCents = tier.PriceCents + riderPortion;
            }

            var entry = new EventWaitlistEntry
            {
                TenantId = _tenantContext.TenantId,
                EventId = req.EventId,
                TierId = req.TierId,
                UserId = userId,
                Quantity = 1,
                Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim(),
                Status = "waiting",
                IsPrepaid = false,
                PrepayAmountCents = prepayAmountCents,
            };
            (Guid id, int position) created;
            try
            {
                created = await _waitlist.Enqueue(entry);
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
            {
                return new ApiResponses().BadRequestResult("You're already on this waitlist.");
            }

            string? clientSecret = null;
            if (req.Prepay && prepayAmountCents > 0)
            {
                var metadata = new Dictionary<string, string>
                {
                    ["tenant_id"] = _tenantContext.TenantId.ToString(),
                    ["sale_kind"] = "waitlist_prepay",
                    ["waitlist_id"] = created.id.ToString(),
                    ["event_id"] = req.EventId.ToString(),
                    ["user_id"] = userId.ToString(),
                };
                if (req.TierId.HasValue) metadata["tier_id"] = req.TierId.Value.ToString();
                try
                {
                    var pi = await _payments.CreatePaymentIntentAsync(
                        amountCents: prepayAmountCents,
                        currency: "usd",
                        metadata: metadata,
                        receiptEmail: user.Email,
                        ct: ct);
                    await _waitlist.SetPrepayPaymentIntentId(created.id, pi.IntentId);
                    clientSecret = pi.ClientSecret;
                }
                catch (InvalidOperationException ex)
                {
                    // Don't roll back the waitlist row — rider is still queued, just not pre-paid.
                    return new ApiResponses().BadRequestResult($"Joined waitlist but pre-pay setup failed: {ex.Message}");
                }
            }

            return new ApiResponses().OkResult(new JoinWaitlistResponse
            {
                WaitlistId = created.id,
                Position = created.position,
                IsPrepaid = false,
                ClientSecret = clientSecret,
                PrepayAmountCents = prepayAmountCents,
                NotifyPhone = user.Phone,
            });
        }

        [HttpGet("Mine")]
        public async Task<IActionResult> ListMine()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!Guid.TryParse(User.FindFirst("UserId")?.Value, out var userId))
                return new ApiResponses().BadRequestResult("Invalid token.");

            var rows = await _waitlist.ListMine(userId, _tenantContext.TenantId);
            // N is small; per-row event/tier hydration is fine for MVP.
            var responses = new List<MyWaitlistEntryResponse>();
            foreach (var w in rows)
            {
                var ev = await _events.GetById(w.EventId, _tenantContext.TenantId);
                if (ev is null) continue;
                var tier = w.TierId.HasValue ? await _tiers.GetById(w.TierId.Value, _tenantContext.TenantId) : null;
                var ahead = w.Status == "waiting" ? await _waitlist.CountAhead(w.EventId, w.TierId, w.Position) : 0;
                responses.Add(new MyWaitlistEntryResponse
                {
                    Id = w.Id,
                    EventId = w.EventId,
                    EventTitle = ev.Title,
                    EventStartsAtUtc = DateTime.SpecifyKind(ev.StartsAt, DateTimeKind.Utc),
                    TierId = w.TierId,
                    TierName = tier?.Name,
                    Position = w.Position,
                    AheadOfMe = ahead,
                    IsPrepaid = w.IsPrepaid,
                    PrepayAmountCents = w.PrepayAmountCents,
                    Status = w.Status,
                    ConfirmDeadlineUtc = w.ConfirmDeadlineUtc,
                    ConfirmToken = w.Status == "promoted" ? w.ConfirmToken : null,
                    CreatedAtUtc = DateTime.SpecifyKind(w.CreatedAt, DateTimeKind.Utc),
                });
            }
            return new ApiResponses().OkResult(responses);
        }

        // ── Confirm flow (rider lands here from the SMS link) ─────────────────
        [HttpGet("Confirm/{token:guid}")]
        public async Task<IActionResult> ConfirmDetails(Guid token)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!Guid.TryParse(User.FindFirst("UserId")?.Value, out var userId))
                return new ApiResponses().BadRequestResult("Invalid token.");

            var entry = await _waitlist.GetByConfirmToken(token);
            if (entry is null || entry.UserId != userId || entry.TenantId != _tenantContext.TenantId)
                return new ApiResponses().NotFoundResult("Confirm link not found or already used.");

            var ev = await _events.GetById(entry.EventId, _tenantContext.TenantId);
            if (ev is null) return new ApiResponses().NotFoundResult("Event not found.");

            var resp = new ConfirmDetailsResponse
            {
                WaitlistId = entry.Id,
                Status = entry.Status,
                EventId = entry.EventId,
                EventTitle = ev.Title,
                EventStartsAtUtc = DateTime.SpecifyKind(ev.StartsAt, DateTimeKind.Utc),
                EventLocationLabel = ev.LocationLabel,
                TierId = entry.TierId,
                IsPrepaid = entry.IsPrepaid,
                PrepayAmountCents = entry.PrepayAmountCents,
                ConfirmDeadlineUtc = entry.ConfirmDeadlineUtc,
            };

            if (entry.TierId.HasValue)
            {
                var tier = await _tiers.GetById(entry.TierId.Value, _tenantContext.TenantId);
                if (tier is not null)
                {
                    resp.TierName = tier.Name;
                    resp.TierPriceCents = tier.PriceCents;
                }
            }

            // Pre-paid + already auto-confirmed: surface the redemption token so the
            // page can deep-link the rider to their QR.
            if (entry.Status == "confirmed" && entry.CreatedPurchaseId.HasValue
                && entry.CreatedPurchaseKind == "event_ticket")
            {
                var purchase = await _ticketPurchases.GetById(entry.CreatedPurchaseId.Value, _tenantContext.TenantId);
                if (purchase is not null) resp.CreatedPurchaseRedemptionToken = purchase.RedemptionToken;
            }
            return new ApiResponses().OkResult(resp);
        }

        [HttpPost("Confirm/{token:guid}/Pay")]
        public async Task<IActionResult> ConfirmAndPay(Guid token, [FromBody] ConfirmPayRequest req, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!Guid.TryParse(User.FindFirst("UserId")?.Value, out var userId))
                return new ApiResponses().BadRequestResult("Invalid token.");

            var entry = await _waitlist.GetByConfirmToken(token);
            if (entry is null || entry.UserId != userId || entry.TenantId != _tenantContext.TenantId)
                return new ApiResponses().NotFoundResult("Confirm link not found or already used.");
            if (entry.Status != "promoted")
                return new ApiResponses().BadRequestResult($"This waitlist entry is {entry.Status} — nothing to confirm.");
            if (entry.ConfirmDeadlineUtc.HasValue && entry.ConfirmDeadlineUtc.Value < DateTime.UtcNow)
                return new ApiResponses().BadRequestResult("This confirm window has expired. The spot has already rolled to the next person.");

            var user = await _users.GetById(userId);
            if (user is null) return new ApiResponses().BadRequestResult("User not found.");

            var ev = await _events.GetById(entry.EventId, _tenantContext.TenantId);
            if (ev is null) return new ApiResponses().BadRequestResult("Event not available.");

            // ── Tier-based: charge tier price + service charge ───────────────
            if (entry.TierId.HasValue)
            {
                var tier = await _tiers.GetById(entry.TierId.Value, _tenantContext.TenantId);
                if (tier is null) return new ApiResponses().BadRequestResult("Selected tier not found.");

                var serviceChargePerUnit = (int)((long)tier.PriceCents * _tenantContext.Tenant.ServiceChargeBps / 10_000L);
                var riderPortion = (int)((long)serviceChargePerUnit * tier.RiderPaidServiceChargeBps / 10_000L);
                var amountCents = tier.PriceCents + riderPortion;

                // Create the ticket purchase row in 'pending' state — the existing webhook
                // flow will flip it to 'paid' on PI succeeded. Mark waitlist confirmed
                // optimistically so the spot is locked in even if rider abandons mid-pay.
                var purchase = new EventTicketPurchase
                {
                    TenantId = _tenantContext.TenantId,
                    TierId = tier.Id,
                    PurchaserUserId = userId,
                    AmountCents = amountCents,
                    ServiceChargeCents = serviceChargePerUnit,
                    PaymentMethod = "stripe",
                    Status = "pending",
                    PurchaserEmail = user.Email,
                    PurchaserName = $"{user.FirstName} {user.LastName}".Trim(),
                };
                var created = await _ticketPurchases.Create(purchase);

                var metadata = new Dictionary<string, string>
                {
                    ["tenant_id"] = _tenantContext.TenantId.ToString(),
                    ["sale_kind"] = "event_ticket",
                    ["ticket_purchase_ids"] = created.Id.ToString(),
                    ["waitlist_id"] = entry.Id.ToString(),
                    ["user_id"] = userId.ToString(),
                };
                var pi = await _payments.CreatePaymentIntentAsync(amountCents, "usd", metadata, user.Email, ct);
                await _ticketPurchases.SetStripePaymentIntentId(created.Id, pi.IntentId);
                await _waitlist.MarkConfirmed(entry.Id, created.Id, "event_ticket");
                return new ApiResponses().OkResult(new ConfirmPayResponse { ClientSecret = pi.ClientSecret, AmountCents = amountCents });
            }

            // Every waitlist entry is tier-based now (the admission that was full), so a
            // promotion always charges that tier above. A tier-less entry is legacy/invalid.
            return new ApiResponses().BadRequestResult("This waitlist entry has no admission to confirm.");
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!Guid.TryParse(User.FindFirst("UserId")?.Value, out var userId))
                return new ApiResponses().BadRequestResult("Invalid token.");

            var entry = await _waitlist.GetById(id);
            if (entry is null || entry.UserId != userId || entry.TenantId != _tenantContext.TenantId)
                return new ApiResponses().NotFoundResult("Waitlist entry not found.");
            if (entry.Status is not "waiting" and not "promoted")
                return new ApiResponses().BadRequestResult($"Can't withdraw from a {entry.Status} entry.");

            await _waitlist.MarkCancelled(id, "rider_withdrew");

            // Refund pre-pay if applicable.
            if (entry.IsPrepaid && !string.IsNullOrEmpty(entry.PrepayPiId))
            {
                try
                {
                    var refund = await _payments.RefundAsync(entry.PrepayPiId!, entry.PrepayAmountCents,
                        idempotencyKey: $"refund-waitlist-{id}-{entry.PrepayAmountCents}", ct: ct);
                    await _waitlist.SetPrepayRefund(id, refund.RefundId, DateTime.UtcNow);
                }
                catch
                {
                    // Surface the failure in logs but keep the cancel; admin can retry from Stripe.
                }
            }
            return new ApiResponses().OkResult(new { id, status = "cancelled" });
        }
    }
}
