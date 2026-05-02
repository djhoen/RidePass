using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Payments;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Data.UserData;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Counter;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    /// <summary>
    /// In-person POS for tenant staff: look up or create a rider, build a cart of any
    /// sellable item, sign the waiver on the rider's behalf, charge a single PaymentIntent.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = TenantPermissions.Policy.SalesCounter)]
    public class CounterController : ControllerBase
    {
        private readonly IUserRepository _users;
        private readonly IWaiverRepository _waivers;
        private readonly IDayPassProductRepository _products;
        private readonly IDayPassPurchaseRepository _passPurchases;
        private readonly IEventTicketTierRepository _tiers;
        private readonly IEventTicketPurchaseRepository _ticketPurchases;
        private readonly IPaymentProvider _payments;
        private readonly IPasswordHasher<User> _passwordHasher;
        private readonly ITenantContext _tenantContext;

        public CounterController(
            IUserRepository users,
            IWaiverRepository waivers,
            IDayPassProductRepository products,
            IDayPassPurchaseRepository passPurchases,
            IEventTicketTierRepository tiers,
            IEventTicketPurchaseRepository ticketPurchases,
            IPaymentProvider payments,
            IPasswordHasher<User> passwordHasher,
            ITenantContext tenantContext)
        {
            _users = users;
            _waivers = waivers;
            _products = products;
            _passPurchases = passPurchases;
            _tiers = tiers;
            _ticketPurchases = ticketPurchases;
            _payments = payments;
            _passwordHasher = passwordHasher;
            _tenantContext = tenantContext;
        }

        [HttpPost("Riders/Find")]
        public async Task<IActionResult> FindRider([FromBody] RiderLookupRequest request)
        {
            if (!_tenantContext.IsResolved)
            {
                return new ApiResponses().BadRequestResult("No tenant resolved.");
            }

            var rider = await _users.GetGlobalByEmail(request.Email.Trim());
            if (rider is null || rider.Role != "rider")
            {
                return new ApiResponses().NotFoundResult("No rider with that email.");
            }

            var activeWaiver = await _waivers.GetActive(_tenantContext.TenantId);
            bool signedCurrent = true;
            DateTime? signedAt = null;
            if (activeWaiver is not null)
            {
                var sig = await _waivers.GetSignature(rider.Id, activeWaiver.Id);
                signedCurrent = sig is not null;
                if (sig is not null)
                {
                    signedAt = DateTime.SpecifyKind(sig.SignedAt, DateTimeKind.Utc);
                }
            }

            return new ApiResponses().OkResult(new RiderLookupResponse
            {
                Id = rider.Id,
                Email = rider.Email,
                FirstName = rider.FirstName,
                LastName = rider.LastName,
                HasSignedCurrentWaiver = signedCurrent,
                WaiverSignedAtUtc = signedAt,
            });
        }

        [HttpPost("Riders")]
        public async Task<IActionResult> CreateRider([FromBody] CreateCounterRiderRequest request)
        {
            if (!_tenantContext.IsResolved)
            {
                return new ApiResponses().BadRequestResult("No tenant resolved.");
            }

            var email = request.Email.Trim();
            if (await _users.GetGlobalByEmail(email) is not null)
            {
                return new ApiResponses().BadRequestResult("A rider with that email already exists — use Find instead.");
            }

            // Random unguessable password the rider never sees. They claim the account later via reset.
            Span<byte> bytes = stackalloc byte[32];
            RandomNumberGenerator.Fill(bytes);
            var unknownPassword = Convert.ToHexString(bytes);

            var rider = new User
            {
                TenantId = null,         // Riders are global — one account works at every tenant.
                Email = email,
                FirstName = request.FirstName.Trim(),
                LastName = request.LastName.Trim(),
                Role = "rider",
                Status = "active",
            };
            rider.PasswordHash = _passwordHasher.HashPassword(rider, unknownPassword);
            rider.Id = await _users.Create(rider);

            return new ApiResponses().OkResult(new CreateCounterRiderResponse
            {
                Id = rider.Id,
                Email = rider.Email,
                FirstName = rider.FirstName,
                LastName = rider.LastName,
            });
        }

        [HttpPost("Sale")]
        public async Task<IActionResult> CreateSale([FromBody] CounterSaleRequest request, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved)
            {
                return new ApiResponses().BadRequestResult("No tenant resolved.");
            }

            var rider = await _users.GetById(request.RiderId);
            if (rider is null || rider.Role != "rider")
            {
                return new ApiResponses().BadRequestResult("Rider not found.");
            }

            // Sign waiver on rider's behalf if requested and not already signed.
            Guid? waiverSignatureId = null;
            var activeWaiver = await _waivers.GetActive(_tenantContext.TenantId);
            if (activeWaiver is not null)
            {
                var existing = await _waivers.GetSignature(rider.Id, activeWaiver.Id);
                if (existing is null)
                {
                    if (!request.SignWaiver)
                    {
                        return new ApiResponses().BadRequestResult("Rider has not signed the active waiver.");
                    }
                    var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
                    waiverSignatureId = await _waivers.Sign(_tenantContext.TenantId, rider.Id, activeWaiver.Id, ip);
                }
                else
                {
                    waiverSignatureId = existing.Id;
                }
            }

            // Validate every cart item up front and compute total before writing anything.
            var dayPassItems = new List<(CounterCartItem Item, DayPassProduct Product)>();
            var ticketItems = new List<(CounterCartItem Item, EventTicketTier Tier)>();
            int totalCents = 0;
            foreach (var item in request.Items)
            {
                if (item.Kind == "day_pass")
                {
                    var product = await _products.GetById(item.ItemId, _tenantContext.TenantId);
                    if (product is null || !product.IsActive)
                    {
                        return new ApiResponses().BadRequestResult($"Day pass product {item.ItemId} is not available.");
                    }
                    dayPassItems.Add((item, product));
                    totalCents += product.PriceCents * item.Quantity;
                }
                else if (item.Kind == "event_ticket")
                {
                    var tier = await _tiers.GetById(item.ItemId, _tenantContext.TenantId);
                    if (tier is null || !tier.IsActive)
                    {
                        return new ApiResponses().BadRequestResult($"Ticket tier {item.ItemId} is not available.");
                    }
                    if (tier.Inventory.HasValue)
                    {
                        var sold = await _tiers.SoldCount(tier.Id);
                        if (sold + item.Quantity > tier.Inventory.Value)
                        {
                            return new ApiResponses().BadRequestResult($"Tier '{tier.Name}' has only {tier.Inventory.Value - sold} left.");
                        }
                    }
                    ticketItems.Add((item, tier));
                    totalCents += tier.PriceCents * item.Quantity;
                }
                else
                {
                    return new ApiResponses().BadRequestResult($"Unsupported cart item kind: {item.Kind}");
                }
            }
            if (totalCents <= 0)
            {
                return new ApiResponses().BadRequestResult("Cart total must be positive.");
            }

            var purchaserName = $"{rider.FirstName} {rider.LastName}".Trim();
            var lineItems = new List<CounterSaleLineItem>();

            foreach (var (item, product) in dayPassItems)
            {
                var p = new DayPassPurchase
                {
                    TenantId = _tenantContext.TenantId,
                    PurchaserUserId = rider.Id,
                    ProductId = product.Id,
                    WaiverSignatureId = waiverSignatureId,
                    Quantity = item.Quantity,
                    AmountCents = product.PriceCents * item.Quantity,
                    Status = "pending",
                    PurchaserEmail = rider.Email,
                    PurchaserName = purchaserName,
                };
                var created = await _passPurchases.Create(p);
                lineItems.Add(new CounterSaleLineItem
                {
                    Kind = "day_pass",
                    PurchaseId = created.Id,
                    RedemptionToken = created.RedemptionToken,
                    DisplayName = product.Name,
                    Quantity = item.Quantity,
                    UnitPriceCents = product.PriceCents,
                    LineAmountCents = product.PriceCents * item.Quantity,
                });
            }
            foreach (var (item, tier) in ticketItems)
            {
                // One row per ticket so each gets its own redemption_token / QR.
                for (int i = 0; i < item.Quantity; i++)
                {
                    var t = new EventTicketPurchase
                    {
                        TenantId = _tenantContext.TenantId,
                        TierId = tier.Id,
                        PurchaserUserId = rider.Id,
                        AmountCents = tier.PriceCents,
                        Status = "pending",
                        PurchaserEmail = rider.Email,
                        PurchaserName = purchaserName,
                    };
                    var created = await _ticketPurchases.Create(t);
                    lineItems.Add(new CounterSaleLineItem
                    {
                        Kind = "event_ticket",
                        PurchaseId = created.Id,
                        RedemptionToken = created.RedemptionToken,
                        DisplayName = tier.Name,
                        Quantity = 1,
                        UnitPriceCents = tier.PriceCents,
                        LineAmountCents = tier.PriceCents,
                    });
                }
            }

            var metadata = new Dictionary<string, string>
            {
                ["tenant_id"] = _tenantContext.TenantId.ToString(),
                ["rider_id"] = rider.Id.ToString(),
                ["sale_kind"] = "counter",
                ["item_count"] = lineItems.Count.ToString(),
            };

            PaymentIntentCreated intent;
            try
            {
                intent = await _payments.CreatePaymentIntentAsync(
                    amountCents: totalCents,
                    currency: "usd",
                    metadata: metadata,
                    receiptEmail: rider.Email,
                    ct: ct);
            }
            catch (InvalidOperationException ex)
            {
                return new ApiResponses().BadRequestResult(ex.Message);
            }

            // Stamp the same PaymentIntent id onto every line item so the webhook can finalize them all.
            foreach (var li in lineItems)
            {
                if (li.Kind == "day_pass")
                {
                    await _passPurchases.SetStripePaymentIntentId(li.PurchaseId, intent.IntentId);
                }
                else
                {
                    await _ticketPurchases.SetStripePaymentIntentId(li.PurchaseId, intent.IntentId);
                }
            }

            return new ApiResponses().OkResult(new CounterSaleResponse
            {
                ClientSecret = intent.ClientSecret,
                TotalAmountCents = totalCents,
                LineItems = lineItems,
            });
        }
    }
}
