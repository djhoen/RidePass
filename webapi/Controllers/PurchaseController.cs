using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Payments;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Purchase;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PurchaseController : ControllerBase
    {
        private readonly IDayPassProductRepository _products;
        private readonly IDayPassPurchaseRepository _purchases;
        private readonly IWaiverRepository _waivers;
        private readonly IUserRepository _users;
        private readonly IEventRepository _events;
        private readonly IEventTicketTierRepository _tiers;
        private readonly IEventTicketPurchaseRepository _ticketPurchases;
        private readonly IDisputeRepository _disputes;
        private readonly IPaymentProvider _payments;
        private readonly ITenantContext _tenantContext;

        public PurchaseController(
            IDayPassProductRepository products,
            IDayPassPurchaseRepository purchases,
            IWaiverRepository waivers,
            IUserRepository users,
            IEventRepository events,
            IEventTicketTierRepository tiers,
            IEventTicketPurchaseRepository ticketPurchases,
            IDisputeRepository disputes,
            IPaymentProvider payments,
            ITenantContext tenantContext)
        {
            _products = products;
            _purchases = purchases;
            _waivers = waivers;
            _users = users;
            _events = events;
            _tiers = tiers;
            _ticketPurchases = ticketPurchases;
            _disputes = disputes;
            _payments = payments;
            _tenantContext = tenantContext;
        }

        [Authorize]
        [HttpPost("DayPass")]
        public async Task<IActionResult> BuyDayPass([FromBody] CreatePurchaseRequest request, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved)
            {
                return new ApiResponses().BadRequestResult("No tenant resolved.");
            }

            if (!TryGetUserId(out var userId))
            {
                return new ApiResponses().BadRequestResult("Invalid token.");
            }

            var user = await _users.GetById(userId);
            // Riders are global (tenant_id null) and may buy at any tenant. Tenant-scoped users
            // (tenant_admin/tenant_staff) are pinned to their home tenant.
            if (user is null || (user.TenantId.HasValue && user.TenantId != _tenantContext.TenantId))
            {
                return new ApiResponses().BadRequestResult("User not found in this tenant.");
            }

            var product = await _products.GetById(request.ProductId, _tenantContext.TenantId);
            if (product is null || !product.IsActive)
            {
                return new ApiResponses().BadRequestResult("Product is not available.");
            }

            var quantity = Math.Max(1, request.Quantity);
            var tenant = _tenantContext.Tenant;

            // Reservation flow: if tenant requires it OR the request specifies an event, bind
            // the purchase to that event and enforce capacity.
            Guid? eventId = request.EventId;
            if (tenant.RequireReservationForPasses && !eventId.HasValue)
            {
                return new ApiResponses().BadRequestResult("This tenant requires you to pick a ride day (eventId) for every pass.");
            }

            DateTime? validOnDate = request.ValidOnDate?.Date;

            if (eventId.HasValue)
            {
                var ev = await _events.GetById(eventId.Value, _tenantContext.TenantId);
                if (ev is null || ev.Status != "scheduled")
                {
                    return new ApiResponses().BadRequestResult("Selected event is not available.");
                }
                if (!ev.Capacity.HasValue)
                {
                    return new ApiResponses().BadRequestResult("Selected event is not reservable (no capacity set).");
                }

                var reserved = await _purchases.ActiveSpotsReservedForEvent(eventId.Value);
                var remaining = ev.Capacity.Value - reserved;
                if (quantity > remaining)
                {
                    return new ApiResponses().BadRequestResult(
                        remaining <= 0
                            ? "This event is sold out."
                            : $"Only {remaining} spot{(remaining == 1 ? string.Empty : "s")} left; requested {quantity}.");
                }

                // Auto-set valid_on_date to the event's start date.
                validOnDate = ev.StartsAt.Date;
            }

            // Enforce waiver: current user must have signed the current active waiver.
            var activeWaiver = await _waivers.GetActive(_tenantContext.TenantId);
            Guid? signatureId = null;
            if (activeWaiver is not null)
            {
                var sig = await _waivers.GetSignature(userId, activeWaiver.Id);
                if (sig is null)
                {
                    return new ApiResponses().BadRequestResult("Rider must sign the current waiver before purchasing.");
                }
                signatureId = sig.Id;
            }

            var amountCents = product.PriceCents * quantity;

            var purchase = new DayPassPurchase
            {
                TenantId = _tenantContext.TenantId,
                PurchaserUserId = userId,
                ProductId = product.Id,
                WaiverSignatureId = signatureId,
                ValidOnDate = validOnDate,
                EventId = eventId,
                Quantity = quantity,
                AmountCents = amountCents,
                Status = "pending",
                PurchaserEmail = user.Email,
                PurchaserName = $"{user.FirstName} {user.LastName}".Trim(),
            };
            var createdDay = await _purchases.Create(purchase);
            purchase.Id = createdDay.Id;
            purchase.RedemptionToken = createdDay.RedemptionToken;

            var metadata = new Dictionary<string, string>
            {
                ["tenant_id"] = _tenantContext.TenantId.ToString(),
                ["purchase_id"] = purchase.Id.ToString(),
                ["user_id"] = userId.ToString(),
                ["product_id"] = product.Id.ToString(),
                ["quantity"] = quantity.ToString(),
            };
            if (eventId.HasValue)
            {
                metadata["event_id"] = eventId.Value.ToString();
            }

            PaymentIntentCreated intent;
            try
            {
                intent = await _payments.CreatePaymentIntentAsync(
                    amountCents: amountCents,
                    currency: "usd",
                    metadata: metadata,
                    receiptEmail: user.Email,
                    ct: ct);
            }
            catch (InvalidOperationException ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }

            await _purchases.SetStripePaymentIntentId(purchase.Id, intent.IntentId);

            return new ApiResponses().OkResult(new CreatePurchaseResponse
            {
                PurchaseId = purchase.Id,
                RedemptionToken = purchase.RedemptionToken,
                ClientSecret = intent.ClientSecret,
                AmountCents = amountCents,
            });
        }

        [AllowAnonymous]
        [HttpPost("EventTicket")]
        public async Task<IActionResult> BuyEventTicket([FromBody] CreateTicketPurchaseRequest request, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved)
            {
                return new ApiResponses().BadRequestResult("No tenant resolved.");
            }

            // Auth is optional for event tickets — guests can buy without an account.
            Guid? purchaserUserId = null;
            string purchaserEmail;
            string purchaserName;

            if (User.Identity?.IsAuthenticated == true && TryGetUserId(out var userId))
            {
                var user = await _users.GetById(userId);
                if (user is null || (user.TenantId.HasValue && user.TenantId != _tenantContext.TenantId))
                {
                    return new ApiResponses().BadRequestResult("User not found in this tenant.");
                }
                purchaserUserId = userId;
                purchaserEmail = user.Email;
                purchaserName = $"{user.FirstName} {user.LastName}".Trim();
            }
            else
            {
                if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Name))
                {
                    return new ApiResponses().BadRequestResult("Email and name are required for guest checkout.");
                }
                purchaserEmail = request.Email.Trim();
                purchaserName = request.Name.Trim();
            }

            var tier = await _tiers.GetById(request.TierId, _tenantContext.TenantId);
            if (tier is null || !tier.IsActive)
            {
                return new ApiResponses().BadRequestResult("Tier is not available.");
            }

            if (tier.Inventory.HasValue)
            {
                var sold = await _tiers.SoldCount(tier.Id);
                if (sold >= tier.Inventory.Value)
                {
                    return new ApiResponses().BadRequestResult("This tier is sold out.");
                }
            }

            var purchase = new EventTicketPurchase
            {
                TenantId = _tenantContext.TenantId,
                TierId = tier.Id,
                PurchaserUserId = purchaserUserId,
                AmountCents = tier.PriceCents,
                Status = "pending",
                PurchaserEmail = purchaserEmail,
                PurchaserName = purchaserName,
            };
            var createdTicket = await _ticketPurchases.Create(purchase);
            purchase.Id = createdTicket.Id;
            purchase.RedemptionToken = createdTicket.RedemptionToken;

            var metadata = new Dictionary<string, string>
            {
                ["tenant_id"] = _tenantContext.TenantId.ToString(),
                ["ticket_purchase_id"] = purchase.Id.ToString(),
                ["tier_id"] = tier.Id.ToString(),
                ["event_id"] = tier.EventId.ToString(),
            };
            if (purchaserUserId.HasValue)
            {
                metadata["user_id"] = purchaserUserId.Value.ToString();
            }

            PaymentIntentCreated intent;
            try
            {
                intent = await _payments.CreatePaymentIntentAsync(
                    amountCents: tier.PriceCents,
                    currency: "usd",
                    metadata: metadata,
                    receiptEmail: purchaserEmail,
                    ct: ct);
            }
            catch (InvalidOperationException ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }

            await _ticketPurchases.SetStripePaymentIntentId(purchase.Id, intent.IntentId);

            return new ApiResponses().OkResult(new CreatePurchaseResponse
            {
                PurchaseId = purchase.Id,
                RedemptionToken = purchase.RedemptionToken,
                ClientSecret = intent.ClientSecret,
                AmountCents = tier.PriceCents,
            });
        }

        [Authorize(Policy = TenantPermissions.Policy.SalesView)]
        [HttpGet("Admin")]
        public async Task<IActionResult> ListForAdmin(
            [FromQuery] DateTime? fromUtc,
            [FromQuery] DateTime? toUtc,
            [FromQuery] string? status)
        {
            var rows = await _purchases.ListForAdmin(_tenantContext.TenantId, fromUtc, toUtc, status);
            var response = rows.Select(r => new PurchaseResponse
            {
                Id = r.Id,
                ProductName = r.ProductName,
                PurchaserName = r.PurchaserName,
                PurchaserEmail = r.PurchaserEmail,
                AmountCents = r.AmountCents,
                Status = r.Status,
                ValidOnDate = r.ValidOnDate,
                CreatedAt = DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc),
            });
            return new ApiResponses().OkResult(response);
        }

        [Authorize(Policy = TenantPermissions.Policy.SalesCancel)]
        [HttpPost("DayPass/{id:guid}/Cancel")]
        public async Task<IActionResult> CancelDayPass(Guid id, [FromBody] CancelPurchaseRequest request)
        {
            var existing = await _purchases.GetById(id, _tenantContext.TenantId);
            if (existing is null)
            {
                return new ApiResponses().NotFoundResult("Purchase not found.");
            }
            if (existing.Status != "paid")
            {
                return new ApiResponses().BadRequestResult($"Cannot cancel a purchase with status '{existing.Status}'.");
            }
            if (!TryGetUserId(out var adminId))
            {
                return new ApiResponses().BadRequestResult("Invalid token.");
            }

            await _purchases.Cancel(id, _tenantContext.TenantId, adminId, request.Reason);
            return new ApiResponses().OkResult(new { id, status = "cancelled" });
        }

        [Authorize(Policy = TenantPermissions.Policy.SalesCancel)]
        [HttpPost("Ticket/{id:guid}/Cancel")]
        public async Task<IActionResult> CancelTicket(Guid id, [FromBody] CancelPurchaseRequest request)
        {
            var existing = await _ticketPurchases.GetById(id, _tenantContext.TenantId);
            if (existing is null)
            {
                return new ApiResponses().NotFoundResult("Ticket not found.");
            }
            if (existing.Status != "paid")
            {
                return new ApiResponses().BadRequestResult($"Cannot cancel a ticket with status '{existing.Status}'.");
            }
            if (!TryGetUserId(out var adminId))
            {
                return new ApiResponses().BadRequestResult("Invalid token.");
            }

            await _ticketPurchases.Cancel(id, _tenantContext.TenantId, adminId, request.Reason);
            return new ApiResponses().OkResult(new { id, status = "cancelled" });
        }

        [Authorize(Policy = TenantPermissions.Policy.DisputesView)]
        [HttpGet("Admin/Disputes")]
        public async Task<IActionResult> ListDisputes()
        {
            var rows = await _disputes.ListByTenant(_tenantContext.TenantId);
            var items = rows.Select(d => new TenantDisputeListItem
            {
                Id = d.Id,
                Kind = d.DayPassPurchaseId.HasValue ? "day_pass"
                     : d.EventTicketPurchaseId.HasValue ? "event_ticket"
                     : "unlinked",
                PurchaseId = d.DayPassPurchaseId ?? d.EventTicketPurchaseId,
                ItemName = d.ItemName,
                PurchaserName = d.PurchaserName,
                PurchaserEmail = d.PurchaserEmail,
                StripeDisputeId = d.StripeDisputeId,
                AmountCents = d.AmountCents,
                Currency = d.Currency,
                Reason = d.Reason,
                Status = d.Status,
                EvidenceDueByUtc = d.EvidenceDueBy.HasValue ? DateTime.SpecifyKind(d.EvidenceDueBy.Value, DateTimeKind.Utc) : null,
                StripeCreatedAtUtc = DateTime.SpecifyKind(d.StripeCreatedAt, DateTimeKind.Utc),
                UpdatedAtUtc = DateTime.SpecifyKind(d.UpdatedAt, DateTimeKind.Utc),
            });
            return new ApiResponses().OkResult(items);
        }

        private bool TryGetUserId(out Guid userId)
        {
            var claim = User.FindFirst("UserId")?.Value;
            return Guid.TryParse(claim, out userId);
        }
    }
}
