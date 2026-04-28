using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Payments;
using Services.Repositories.Data.TenantData;
using Services.Repositories.Data.UserData;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Reports;
using webapi.Controllers.API.Data.SuperAdmin;
using webapi.Helpers;

namespace webapi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class SuperAdminController : ControllerBase
    {
        private readonly IUserRepository _users;
        private readonly ITenantRepository _tenants;
        private readonly IDayPassPurchaseRepository _dayPasses;
        private readonly IEventTicketPurchaseRepository _tickets;
        private readonly IDisputeRepository _disputes;
        private readonly IReportsRepository _reports;
        private readonly IPaymentProvider _payments;
        private readonly IPasswordHasher<User> _passwordHasher;
        private readonly IJwtIssuer _jwtIssuer;

        public SuperAdminController(
            IUserRepository users,
            ITenantRepository tenants,
            IDayPassPurchaseRepository dayPasses,
            IEventTicketPurchaseRepository tickets,
            IDisputeRepository disputes,
            IReportsRepository reports,
            IPaymentProvider payments,
            IPasswordHasher<User> passwordHasher,
            IJwtIssuer jwtIssuer)
        {
            _users = users;
            _tenants = tenants;
            _dayPasses = dayPasses;
            _tickets = tickets;
            _disputes = disputes;
            _reports = reports;
            _payments = payments;
            _passwordHasher = passwordHasher;
            _jwtIssuer = jwtIssuer;
        }

        /// <summary>
        /// One-time platform bootstrap. Creates the first super_admin. Refuses after at least
        /// one super_admin exists. Anonymous so the platform can be initialised before there's
        /// anyone to authenticate as.
        /// </summary>
        [AllowAnonymous]
        [HttpPost("Bootstrap")]
        public async Task<IActionResult> Bootstrap([FromBody] BootstrapRequest request)
        {
            if (await _users.AnySuperAdminExists())
            {
                return new ApiResponses().BadRequestResult("A super admin already exists — bootstrap has already run.");
            }

            var user = new User
            {
                TenantId = null,
                Email = request.Email.Trim(),
                FirstName = request.FirstName.Trim(),
                LastName = request.LastName.Trim(),
                Role = "super_admin",
                Status = "active",
            };
            user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);

            user.Id = await _users.Create(user);
            return new ApiResponses().OkResult(new { user.Id, user.Email });
        }

        [Authorize(Policy = SuperAdminRequirement.PolicyName)]
        [HttpGet("Tenants")]
        public async Task<IActionResult> ListTenants()
        {
            var all = await GetAllTenants();
            var items = all.Select(ToTenantListItem).OrderBy(t => t.Subdomain);
            return new ApiResponses().OkResult(items);
        }

        [Authorize(Policy = SuperAdminRequirement.PolicyName)]
        [HttpPost("Tenants")]
        public async Task<IActionResult> CreateTenant([FromBody] CreateTenantRequest request)
        {
            // Validate timezone
            try { TimeZoneInfo.FindSystemTimeZoneById(request.Timezone); }
            catch (TimeZoneNotFoundException)
            {
                return new ApiResponses().BadRequestResult($"Unknown IANA timezone: {request.Timezone}.");
            }

            // Check subdomain uniqueness
            var existing = await _tenants.GetBySubdomain(request.Subdomain);
            if (existing is not null)
            {
                return new ApiResponses().BadRequestResult($"Subdomain '{request.Subdomain}' is already taken.");
            }

            var tenant = new Tenant
            {
                Subdomain = request.Subdomain,
                DisplayName = request.DisplayName,
                Status = "active",
                Timezone = request.Timezone,
            };
            tenant.Id = await _tenants.Create(tenant);

            var response = new CreateTenantResponse
            {
                TenantId = tenant.Id,
                Subdomain = tenant.Subdomain,
                DisplayName = tenant.DisplayName,
                Timezone = tenant.Timezone,
            };

            // Optional: provision the first tenant_admin
            if (!string.IsNullOrWhiteSpace(request.AdminEmail))
            {
                if (string.IsNullOrWhiteSpace(request.AdminFirstName) || string.IsNullOrWhiteSpace(request.AdminLastName))
                {
                    return new ApiResponses().BadRequestResult("AdminFirstName and AdminLastName are required when AdminEmail is provided.");
                }

                var tempPassword = GenerateTemporaryPassword();
                var admin = new User
                {
                    TenantId = tenant.Id,
                    Email = request.AdminEmail.Trim(),
                    FirstName = request.AdminFirstName.Trim(),
                    LastName = request.AdminLastName.Trim(),
                    Role = "tenant_admin",
                    Status = "active",
                };
                admin.PasswordHash = _passwordHasher.HashPassword(admin, tempPassword);
                admin.Id = await _users.Create(admin);

                response.AdminUserId = admin.Id;
                response.AdminEmail = admin.Email;
                response.AdminTemporaryPassword = tempPassword;
            }

            return new ApiResponses().OkResult(response);
        }

        [Authorize(Policy = SuperAdminRequirement.PolicyName)]
        [HttpGet("Users")]
        public async Task<IActionResult> ListUsers([FromQuery] string? q)
        {
            var users = await _users.SearchAll(q, 100);
            var tenantIds = users.Where(u => u.TenantId.HasValue).Select(u => u.TenantId!.Value).Distinct();
            var tenantsById = new Dictionary<Guid, Tenant>();
            foreach (var id in tenantIds)
            {
                var t = await _tenants.GetById(id);
                if (t is not null) tenantsById[id] = t;
            }

            var items = users.Select(u => new SuperAdminUserItem
            {
                Id = u.Id,
                TenantId = u.TenantId,
                TenantSubdomain = u.TenantId.HasValue && tenantsById.TryGetValue(u.TenantId.Value, out var t) ? t.Subdomain : null,
                Email = u.Email,
                FirstName = u.FirstName,
                LastName = u.LastName,
                Role = u.Role,
                Status = u.Status,
            });

            return new ApiResponses().OkResult(items);
        }

        [Authorize(Policy = SuperAdminRequirement.PolicyName)]
        [HttpPost("Impersonate/{userId:guid}")]
        public async Task<IActionResult> Impersonate(Guid userId)
        {
            var target = await _users.GetById(userId);
            if (target is null)
            {
                return new ApiResponses().NotFoundResult("User not found.");
            }
            if (target.Role == "super_admin")
            {
                return new ApiResponses().BadRequestResult("Cannot impersonate another super admin.");
            }

            if (!Guid.TryParse(User.FindFirst("UserId")?.Value, out var currentSuperAdminId))
            {
                return new ApiResponses().BadRequestResult("Invalid super admin token.");
            }

            var token = _jwtIssuer.IssueForUser(
                user: target,
                expiration: TimeSpan.FromHours(1),
                impersonatedBy: currentSuperAdminId);

            string? subdomain = null;
            if (target.TenantId.HasValue)
            {
                var t = await _tenants.GetById(target.TenantId.Value);
                subdomain = t?.Subdomain;
            }

            return new ApiResponses().OkResult(new ImpersonationResponse
            {
                Token = token,
                UserId = target.Id,
                Email = target.Email,
                FirstName = target.FirstName,
                LastName = target.LastName,
                Role = target.Role,
                TenantId = target.TenantId,
                TenantSubdomain = subdomain,
            });
        }

        private async Task<List<Tenant>> GetAllTenants() => await _tenants.ListAll();

        [Authorize(Policy = SuperAdminRequirement.PolicyName)]
        [HttpGet("Refunds")]
        public async Task<IActionResult> ListRefundQueue()
        {
            var dayPasses = await _dayPasses.ListByStatusAcrossTenants("cancelled");
            var tickets = await _tickets.ListByStatusAcrossTenants("cancelled");

            var tenantIds = dayPasses.Select(d => d.TenantId).Concat(tickets.Select(t => t.TenantId)).Distinct().ToList();
            var subdomains = new Dictionary<Guid, string>();
            foreach (var tid in tenantIds)
            {
                var t = await _tenants.GetById(tid);
                if (t is not null) subdomains[tid] = t.Subdomain;
            }

            var items = new List<RefundListItem>();
            items.AddRange(dayPasses.Select(d => new RefundListItem
            {
                Kind = "day_pass",
                Id = d.Id,
                TenantId = d.TenantId,
                TenantSubdomain = subdomains.TryGetValue(d.TenantId, out var s) ? s : "",
                ItemName = d.ProductName + (d.Quantity > 1 ? $" × {d.Quantity}" : ""),
                PurchaserName = d.PurchaserName,
                PurchaserEmail = d.PurchaserEmail,
                AmountCents = d.AmountCents,
                CancellationReason = d.CancellationReason,
                CancelledAtUtc = d.CancelledAt is null ? null : DateTime.SpecifyKind(d.CancelledAt.Value, DateTimeKind.Utc),
                CreatedAtUtc = DateTime.SpecifyKind(d.CreatedAt, DateTimeKind.Utc),
                StripePaymentIntentId = d.StripePaymentIntentId,
            }));
            items.AddRange(tickets.Select(t => new RefundListItem
            {
                Kind = "event_ticket",
                Id = t.Id,
                TenantId = t.TenantId,
                TenantSubdomain = subdomains.TryGetValue(t.TenantId, out var s) ? s : "",
                ItemName = $"{t.EventTitle} — {t.TierName}",
                PurchaserName = t.PurchaserName,
                PurchaserEmail = t.PurchaserEmail,
                AmountCents = t.AmountCents,
                CancellationReason = t.CancellationReason,
                CancelledAtUtc = t.CancelledAt is null ? null : DateTime.SpecifyKind(t.CancelledAt.Value, DateTimeKind.Utc),
                CreatedAtUtc = DateTime.SpecifyKind(t.CreatedAt, DateTimeKind.Utc),
                StripePaymentIntentId = t.StripePaymentIntentId,
            }));

            return new ApiResponses().OkResult(items.OrderByDescending(i => i.CancelledAtUtc ?? i.CreatedAtUtc));
        }

        [Authorize(Policy = SuperAdminRequirement.PolicyName)]
        [HttpPost("Refunds/DayPass/{id:guid}/Process")]
        public async Task<IActionResult> ProcessDayPassRefund(Guid id, CancellationToken ct)
        {
            // Load across all tenants (super admin).
            var all = await _dayPasses.ListByStatusAcrossTenants("cancelled");
            var purchase = all.FirstOrDefault(p => p.Id == id);
            if (purchase is null)
            {
                return new ApiResponses().NotFoundResult("Cancelled purchase not found in refund queue.");
            }
            if (string.IsNullOrEmpty(purchase.StripePaymentIntentId))
            {
                return new ApiResponses().BadRequestResult("Purchase has no Stripe payment_intent to refund.");
            }

            try
            {
                var refund = await _payments.RefundAsync(purchase.StripePaymentIntentId, ct: ct);
                await _dayPasses.MarkRefunded(id, $"stripe_refund={refund.RefundId} status={refund.Status}");
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult($"Stripe refund failed: {ex.Message}");
            }
            return new ApiResponses().OkResult(new { id, status = "refunded" });
        }

        [Authorize(Policy = SuperAdminRequirement.PolicyName)]
        [HttpPost("Refunds/Ticket/{id:guid}/Process")]
        public async Task<IActionResult> ProcessTicketRefund(Guid id, CancellationToken ct)
        {
            var all = await _tickets.ListByStatusAcrossTenants("cancelled");
            var purchase = all.FirstOrDefault(p => p.Id == id);
            if (purchase is null)
            {
                return new ApiResponses().NotFoundResult("Cancelled ticket not found in refund queue.");
            }
            if (string.IsNullOrEmpty(purchase.StripePaymentIntentId))
            {
                return new ApiResponses().BadRequestResult("Ticket has no Stripe payment_intent to refund.");
            }

            try
            {
                var refund = await _payments.RefundAsync(purchase.StripePaymentIntentId, ct: ct);
                await _tickets.MarkRefunded(id, $"stripe_refund={refund.RefundId} status={refund.Status}");
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult($"Stripe refund failed: {ex.Message}");
            }
            return new ApiResponses().OkResult(new { id, status = "refunded" });
        }

        [Authorize(Policy = SuperAdminRequirement.PolicyName)]
        [HttpGet("Disputes")]
        public async Task<IActionResult> ListDisputes()
        {
            var rows = await _disputes.ListAllAcrossTenants();
            var items = rows.Select(d => new DisputeListItem
            {
                Id = d.Id,
                TenantId = d.TenantId,
                TenantSubdomain = d.TenantSubdomain,
                Kind = d.DayPassPurchaseId.HasValue ? "day_pass"
                     : d.EventTicketPurchaseId.HasValue ? "event_ticket"
                     : "unlinked",
                PurchaseId = d.DayPassPurchaseId ?? d.EventTicketPurchaseId,
                ItemName = d.ItemName,
                PurchaserName = d.PurchaserName,
                PurchaserEmail = d.PurchaserEmail,
                StripeDisputeId = d.StripeDisputeId,
                StripePaymentIntentId = d.StripePaymentIntentId,
                StripeChargeId = d.StripeChargeId,
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

        [Authorize(Policy = SuperAdminRequirement.PolicyName)]
        [HttpGet("Analytics")]
        public async Task<IActionResult> GetAnalytics([FromQuery] DateTime fromUtc, [FromQuery] DateTime toUtc)
        {
            if (toUtc <= fromUtc)
            {
                return new ApiResponses().BadRequestResult("toUtc must be after fromUtc.");
            }

            var totals = await _reports.GetPlatformTotals(fromUtc, toUtc);
            var daily = await _reports.GetPlatformDailyRevenue(fromUtc, toUtc);
            var breakdown = await _reports.GetTenantBreakdown(fromUtc, toUtc);

            var response = new PlatformAnalyticsSummary
            {
                FromUtc = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc),
                ToUtc = DateTime.SpecifyKind(toUtc, DateTimeKind.Utc),
                TotalRevenueCents = totals.RevenueCents,
                PassesSold = totals.PassesSold,
                TicketsSold = totals.TicketsSold,
                RefundedCount = totals.RefundedCount,
                DisputedCount = totals.DisputedCount,
                TotalTenants = totals.TotalTenants,
                ActiveTenants = totals.ActiveTenants,
                DailyRevenue = daily.Select(d => new DailyRevenuePointDto
                {
                    Date = d.Date,
                    RevenueCents = d.RevenueCents,
                    PassesSold = d.PassesSold,
                    TicketsSold = d.TicketsSold,
                }).ToList(),
                TenantBreakdown = breakdown.Select(t => new TenantBreakdownDto
                {
                    TenantId = t.TenantId,
                    Subdomain = t.Subdomain,
                    DisplayName = t.DisplayName,
                    PassesSold = t.PassesSold,
                    TicketsSold = t.TicketsSold,
                    RevenueCents = t.RevenueCents,
                    RefundedCount = t.RefundedCount,
                    DisputedCount = t.DisputedCount,
                }).ToList(),
            };

            return new ApiResponses().OkResult(response);
        }

        private static TenantListItem ToTenantListItem(Tenant t) => new()
        {
            Id = t.Id,
            Subdomain = t.Subdomain,
            DisplayName = t.DisplayName,
            Status = t.Status,
            Timezone = t.Timezone,
            CreatedAtUtc = DateTime.SpecifyKind(t.CreatedAt, DateTimeKind.Utc),
        };

        private static string GenerateTemporaryPassword()
        {
            // 12 random bytes → 24-char hex. Enough entropy for a one-time password the admin
            // sees once and must change after first login.
            Span<byte> bytes = stackalloc byte[12];
            RandomNumberGenerator.Fill(bytes);
            return Convert.ToHexString(bytes);
        }
    }
}
