using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Services.Helpers;
using Services.Audit;
using Services.Notifications;
using Services.Payments;
using Services.Repositories.Data.PaymentData;
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
    [Route("api/[controller]")]
    public class SuperAdminController : ControllerBase
    {
        private readonly IUserRepository _users;
        private readonly ITenantRepository _tenants;
        private readonly IEventTicketPurchaseRepository _tickets;
        private readonly IEventTicketTierRepository _ticketTiers;
        private readonly IDisputeRepository _disputes;
        private readonly IReportsRepository _reports;
        private readonly IPaymentProvider _payments;
        private readonly IPasswordHasher<User> _passwordHasher;
        private readonly IJwtIssuer _jwtIssuer;
        private readonly ITenantLedgerRepository _ledger;
        private readonly ITenantPayoutRepository _payouts;
        private readonly INotificationService _notifications;
        private readonly IAuditLogger _audit;
        private readonly IAuditLogRepository _auditRepo;
        private readonly ISmtpEmailer _emailer;
        private readonly ICouponRepository _couponShares;
        private readonly ILogger<SuperAdminController> _logger;
        private readonly IMemoryCache _cache;

        public SuperAdminController(
            IUserRepository users,
            ITenantRepository tenants,
            IEventTicketPurchaseRepository tickets,
            IEventTicketTierRepository ticketTiers,
            IDisputeRepository disputes,
            IReportsRepository reports,
            IPaymentProvider payments,
            IPasswordHasher<User> passwordHasher,
            IJwtIssuer jwtIssuer,
            ITenantLedgerRepository ledger,
            ITenantPayoutRepository payouts,
            INotificationService notifications,
            IAuditLogger audit,
            IAuditLogRepository auditRepo,
            ISmtpEmailer emailer,
            ICouponRepository couponShares,
            ILogger<SuperAdminController> logger,
            IMemoryCache cache)
        {
            _users = users;
            _tenants = tenants;
            _tickets = tickets;
            _ticketTiers = ticketTiers;
            _disputes = disputes;
            _reports = reports;
            _payments = payments;
            _passwordHasher = passwordHasher;
            _jwtIssuer = jwtIssuer;
            _ledger = ledger;
            _payouts = payouts;
            _notifications = notifications;
            _audit = audit;
            _auditRepo = auditRepo;
            _emailer = emailer;
            _couponShares = couponShares;
            _logger = logger;
            _cache = cache;
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
            await _audit.Log("super_admin.bootstrap", $"Bootstrapped first super admin {user.Email}", "user", user.Id);
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
                TenantType = request.TenantType,
                Timezone = request.Timezone,
            };
            tenant.Id = await _tenants.Create(tenant);
            // The DB triggers (seed_default_event_types, seed_initial_waiver,
            // seed_default_pass_products) fired during this insert and read
            // tenant.tenant_type to seed type-appropriate defaults.
            await _audit.Log("tenant.create", $"Created tenant '{tenant.DisplayName}' ({tenant.Subdomain})",
                "tenant", tenant.Id, tenant.Id, new { tenant.Subdomain, tenant.DisplayName, tenant.TenantType, tenant.Timezone });

            var response = new CreateTenantResponse
            {
                TenantId = tenant.Id,
                Subdomain = tenant.Subdomain,
                DisplayName = tenant.DisplayName,
                TenantType = tenant.TenantType,
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

                // Welcome email with the temporary password and a deep link to the tenant subdomain.
                if (_emailer.IsConfigured)
                {
                    var apex = ApexHostFromCurrent(Request.Host.Value);
                    var loginUrl = $"{Request.Scheme}://{tenant.Subdomain}.{apex}/Login";
                    var html = $@"<p>Hi {System.Net.WebUtility.HtmlEncode(admin.FirstName)},</p>
<p>Your RidePass admin account for <strong>{System.Net.WebUtility.HtmlEncode(tenant.DisplayName)}</strong> has been created.</p>
<p><strong>Sign in:</strong> <a href=""{loginUrl}"">{loginUrl}</a><br/>
<strong>Email:</strong> {System.Net.WebUtility.HtmlEncode(admin.Email)}<br/>
<strong>Temporary password:</strong> <code>{tempPassword}</code></p>
<p>For security, please <a href=""{Request.Scheme}://{tenant.Subdomain}.{apex}/ResetPassword"">reset your password</a> after your first sign-in.</p>";
                    var sent = await _emailer.Send(admin.Email, $"Welcome to RidePass, {tenant.DisplayName}", html);
                    if (!sent)
                    {
                        _logger.LogWarning("Welcome email send returned false for tenant {Tenant} admin {Email}", tenant.Subdomain, admin.Email);
                    }
                }
            }

            return new ApiResponses().OkResult(response);
        }

        private static string ApexHostFromCurrent(string currentHost)
        {
            var hostOnly = currentHost.Split(':')[0];
            var parts = hostOnly.Split('.');
            if (parts.Length >= 3) return string.Join('.', parts.Skip(1));
            return currentHost;
        }

        /// <summary>
        /// Create an additional super admin. Caller must already be a super admin.
        /// </summary>
        [Authorize(Policy = SuperAdminRequirement.PolicyName)]
        [HttpPost("SuperAdmins")]
        public async Task<IActionResult> CreateSuperAdmin([FromBody] CreateSuperAdminRequest request)
        {
            var existing = await _users.GetGlobalByEmail(request.Email.Trim());
            if (existing is not null)
            {
                return new ApiResponses().BadRequestResult($"A user with email '{request.Email}' already exists.");
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

            await _audit.Log("super_admin.create", $"Created super admin {user.Email}", "user", user.Id, null, new { user.Email });
            return new ApiResponses().OkResult(new { user.Id, user.Email, user.Role });
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
                Phone = u.Phone,
            });

            return new ApiResponses().OkResult(items);
        }

        [Authorize(Policy = SuperAdminRequirement.PolicyName)]
        [HttpGet("Users/{id:guid}")]
        public async Task<IActionResult> GetUser(Guid id)
        {
            var u = await _users.GetById(id);
            if (u is null)
            {
                return new ApiResponses().NotFoundResult("User not found.");
            }
            string? subdomain = null;
            if (u.TenantId.HasValue)
            {
                var t = await _tenants.GetById(u.TenantId.Value);
                subdomain = t?.Subdomain;
            }
            return new ApiResponses().OkResult(ToUserDetail(u, subdomain));
        }

        [Authorize(Policy = SuperAdminRequirement.PolicyName)]
        [HttpPut("Users/{id:guid}")]
        public async Task<IActionResult> UpdateUser(Guid id, [FromBody] SuperAdminUpdateUserRequest request)
        {
            var u = await _users.GetById(id);
            if (u is null)
            {
                return new ApiResponses().NotFoundResult("User not found.");
            }

            var email = (request.Email ?? "").Trim();
            if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            {
                return new ApiResponses().BadRequestResult("A valid email address is required.");
            }

            var role = (request.Role ?? "").Trim();
            if (!AllowedRoles.Contains(role))
            {
                return new ApiResponses().BadRequestResult($"Unknown role '{role}'.");
            }
            var status = (request.Status ?? "").Trim();
            if (!AllowedStatuses.Contains(status))
            {
                return new ApiResponses().BadRequestResult($"Unknown status '{status}'.");
            }
            // Super admins are global; a tenant-scoped user can't hold that role and vice versa.
            if (role == "super_admin" && u.TenantId.HasValue)
            {
                return new ApiResponses().BadRequestResult("A tenant user can't be made a super admin (super admins are global).");
            }
            if (role != "super_admin" && !u.TenantId.HasValue && u.Role == "super_admin")
            {
                return new ApiResponses().BadRequestResult("A global super admin can't be reassigned to a tenant role here.");
            }

            // Email is the login; block collisions within the same scope (global pool for
            // riders/super admins, tenant pool for tenant users).
            if (!string.Equals(email, u.Email, StringComparison.OrdinalIgnoreCase))
            {
                var conflict = u.TenantId.HasValue
                    ? await _users.GetByEmail(u.TenantId.Value, email)
                    : await _users.GetGlobalByEmail(email);
                if (conflict is not null && conflict.Id != u.Id)
                {
                    return new ApiResponses().BadRequestResult($"Another user already has the email '{email}'.");
                }
            }

            u.Email = email;
            u.FirstName = (request.FirstName ?? "").Trim();
            u.LastName = (request.LastName ?? "").Trim();
            u.Role = role;
            u.Status = status;
            u.Phone = NullIfBlank(request.Phone);
            u.Birthdate = request.Birthdate;
            u.EmergencyContactName = NullIfBlank(request.EmergencyContactName);
            u.EmergencyContactPhone = NullIfBlank(request.EmergencyContactPhone);
            u.AddressLine = NullIfBlank(request.AddressLine);
            u.AddressLine2 = NullIfBlank(request.AddressLine2);
            u.City = NullIfBlank(request.City);
            u.State = NullIfBlank(request.State);
            u.PostalCode = NullIfBlank(request.PostalCode);
            u.Country = NullIfBlank(request.Country);
            u.Bike = NullIfBlank(request.Bike);
            u.RaceNumber = NullIfBlank(request.RaceNumber);
            u.EmailVerified = request.EmailVerified;

            await _users.SuperAdminUpdateUser(u);
            await _audit.Log("super_admin.user_update", $"Updated user {u.Email}", "user", u.Id);

            string? subdomain = null;
            if (u.TenantId.HasValue)
            {
                var t = await _tenants.GetById(u.TenantId.Value);
                subdomain = t?.Subdomain;
            }
            return new ApiResponses().OkResult(ToUserDetail(u, subdomain));
        }

        private static readonly HashSet<string> AllowedRoles =
            new(StringComparer.Ordinal) { "rider", "tenant_admin", "tenant_staff", "super_admin" };
        private static readonly HashSet<string> AllowedStatuses =
            new(StringComparer.Ordinal) { "active", "suspended", "pending" };

        private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        private static SuperAdminUserDetail ToUserDetail(User u, string? subdomain) => new()
        {
            Id = u.Id,
            TenantId = u.TenantId,
            TenantSubdomain = subdomain,
            Email = u.Email,
            FirstName = u.FirstName,
            LastName = u.LastName,
            Role = u.Role,
            Status = u.Status,
            Phone = u.Phone,
            Birthdate = u.Birthdate,
            EmergencyContactName = u.EmergencyContactName,
            EmergencyContactPhone = u.EmergencyContactPhone,
            AddressLine = u.AddressLine,
            AddressLine2 = u.AddressLine2,
            City = u.City,
            State = u.State,
            PostalCode = u.PostalCode,
            Country = u.Country,
            Bike = u.Bike,
            RaceNumber = u.RaceNumber,
            EmailVerified = u.EmailVerified,
            CreatedAtUtc = DateTime.SpecifyKind(u.CreatedAt, DateTimeKind.Utc),
        };

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
            var tickets = await _tickets.ListByStatusAcrossTenants("cancelled");

            var tenantIds = tickets.Select(t => t.TenantId).Distinct().ToList();
            var subdomains = new Dictionary<Guid, string>();
            foreach (var tid in tenantIds)
            {
                var t = await _tenants.GetById(tid);
                if (t is not null) subdomains[tid] = t.Subdomain;
            }

            var items = new List<RefundListItem>();
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

            var tier = await _ticketTiers.GetById(purchase.TierId, purchase.TenantId);
            var riderBps = tier?.RiderPaidServiceChargeBps ?? 10000;
            var refundCents = Services.Helpers.RefundCalculator.RefundableCents(
                purchase.AmountCents, purchase.ServiceChargeCents, riderBps);
            if (refundCents <= 0)
            {
                return new ApiResponses().BadRequestResult("Nothing to refund (service charge already withheld).");
            }

            try
            {
                var refund = await _payments.RefundAsync(purchase.StripePaymentIntentId, refundCents,
                    idempotencyKey: $"refund-ticket-{id}-{refundCents}", ct: ct);
                await _tickets.MarkRefunded(id, $"stripe_refund={refund.RefundId} status={refund.Status} amount_cents={refundCents}");
                await WriteRefundLedgerEntry(purchase.TenantId, "event_ticket", id, refund.RefundId);
                var amount = $"${(refundCents / 100m):0.00}";
                await _audit.Log("refund.process", $"Refunded event ticket {amount} for {purchase.PurchaserEmail}",
                    "event_ticket_purchase", id, purchase.TenantId, new { refund.RefundId, refundCents, purchase.AmountCents });
                await _notifications.EmitToTenantAdmins(purchase.TenantId, "refund_processed",
                    $"Refund issued: {amount}",
                    $"A {amount} refund was issued for {purchase.PurchaserName} ({purchase.PurchaserEmail}).",
                    "/Admin/Purchases");
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult($"Stripe refund failed: {ex.Message}");
            }
            return new ApiResponses().OkResult(new { id, status = "refunded", refundCents });
        }

        /// <summary>
        /// Writes a negative ledger entry that mirrors the original sale entry, so the tenant's balance
        /// and our lifetime totals back out the refunded transaction.
        /// </summary>
        private async Task WriteRefundLedgerEntry(Guid tenantId, string sourceKind, Guid sourceId, string stripeRefundId)
        {
            var sale = await _ledger.GetSaleEntryForSource(tenantId, sourceKind, sourceId);
            if (sale is null) return;
            await _ledger.Insert(new TenantLedgerEntry
            {
                TenantId = tenantId,
                EntryKind = "refund",
                SourceKind = sourceKind,
                SourceId = sourceId,
                OccurredAtUtc = DateTime.UtcNow,
                GrossCents = -sale.GrossCents,
                StripeFeeCents = -sale.StripeFeeCents,
                RidepassCutCents = -sale.RidepassCutCents,
                NetToTenantCents = -sale.NetToTenantCents,
                AppliedTierId = sale.AppliedTierId,
                StripePaymentIntentId = sale.StripePaymentIntentId,
                Memo = $"Refund {stripeRefundId}",
            });
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
                Kind = d.EventTicketPurchaseId.HasValue ? "event_ticket" : "unlinked",
                PurchaseId = d.EventTicketPurchaseId,
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
        [HttpGet("Balances")]
        public async Task<IActionResult> ListTenantBalances()
        {
            var summaries = await _ledger.GetSummariesForAllTenants();
            return new ApiResponses().OkResult(summaries);
        }

        [Authorize(Policy = SuperAdminRequirement.PolicyName)]
        [HttpPut("Tenants/{tenantId:guid}/ServiceCharge")]
        public async Task<IActionResult> UpdateTenantServiceCharge(Guid tenantId, [FromBody] UpdateTenantServiceChargeRequest request)
        {
            var tenant = await _tenants.GetById(tenantId);
            if (tenant is null)
            {
                return new ApiResponses().NotFoundResult("Tenant not found.");
            }

            await _tenants.UpdateServiceCharge(tenantId, request.ServiceChargeBps, request.MonthlyServiceChargeCapCents);
            await _audit.Log("tenant.serviceCharge.update",
                $"Set service charge to {request.ServiceChargeBps / 100m:0.##}% (cap {(request.MonthlyServiceChargeCapCents.HasValue ? "$" + request.MonthlyServiceChargeCapCents.Value / 100m : "none")}) for {tenant.Subdomain}",
                "tenant", tenantId, tenantId,
                new { request.ServiceChargeBps, request.MonthlyServiceChargeCapCents });

            return new ApiResponses().OkResult(new
            {
                tenantId,
                serviceChargeBps = request.ServiceChargeBps,
                monthlyServiceChargeCapCents = request.MonthlyServiceChargeCapCents,
            });
        }

        // Platform-level per-tenant concessions switch. Writes the same flag the tenant's own
        // Settings -> Features toggle uses, so support can enable/disable it for any tenant.
        [Authorize(Policy = SuperAdminRequirement.PolicyName)]
        [HttpPut("Tenants/{tenantId:guid}/ConcessionsEnabled")]
        public async Task<IActionResult> UpdateTenantConcessionsEnabled(
            Guid tenantId, [FromBody] webapi.Controllers.API.Data.Tenant.UpdateConcessionsEnabledRequest request)
        {
            var tenant = await _tenants.GetById(tenantId);
            if (tenant is null) return new ApiResponses().NotFoundResult("Tenant not found.");
            await _tenants.UpdateConcessionsEnabled(tenantId, request.Enabled);
            await _audit.Log("tenant.concessions.update",
                $"{(request.Enabled ? "Enabled" : "Disabled")} concessions for {tenant.Subdomain}",
                "tenant", tenantId, tenantId, new { request.Enabled });
            return new ApiResponses().OkResult(new { tenantId, concessionsEnabled = request.Enabled });
        }

        [Authorize(Policy = SuperAdminRequirement.PolicyName)]
        [HttpPut("Tenants/{tenantId:guid}")]
        public async Task<IActionResult> UpdateTenant(Guid tenantId, [FromBody] SuperAdminUpdateTenantRequest request)
        {
            var tenant = await _tenants.GetById(tenantId);
            if (tenant is null) return new ApiResponses().NotFoundResult("Tenant not found.");

            try { TimeZoneInfo.FindSystemTimeZoneById(request.Timezone); }
            catch (TimeZoneNotFoundException)
            {
                return new ApiResponses().BadRequestResult($"Unknown IANA timezone: {request.Timezone}.");
            }

            if (string.IsNullOrWhiteSpace(request.DisplayName))
            {
                return new ApiResponses().BadRequestResult("Display name is required.");
            }

            static string? Norm(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

            await _tenants.UpdateAdminDetails(tenantId,
                request.DisplayName.Trim(), request.Status, request.Timezone, request.IsPublished,
                Norm(request.AddressLine), Norm(request.City), Norm(request.Region),
                Norm(request.PostalCode), Norm(request.Country),
                request.Latitude, request.Longitude,
                Norm(request.ContactEmail), Norm(request.Phone), Norm(request.LoampassMxDestinationId));
            await _tenants.UpdateServiceCharge(tenantId, request.ServiceChargeBps, request.MonthlyServiceChargeCapCents);

            // Evict the cached tenant so changes (especially publish status) take
            // effect immediately instead of after the 5-minute resolution cache.
            _cache.Remove($"tenant:{tenant.Subdomain.ToLowerInvariant()}");

            await _audit.Log("tenant.update",
                $"Updated details for {tenant.Subdomain}",
                "tenant", tenantId, tenantId,
                new { request.DisplayName, request.Status, request.Timezone, request.IsPublished, request.City, request.Region, request.ServiceChargeBps, request.MonthlyServiceChargeCapCents });

            return new ApiResponses().OkResult(new { tenantId });
        }

        [Authorize(Policy = SuperAdminRequirement.PolicyName)]
        [HttpGet("Tenants/{tenantId:guid}/Ledger")]
        public async Task<IActionResult> ListTenantLedger(Guid tenantId, [FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc, [FromQuery] int take = 200)
        {
            var entries = await _ledger.ListByTenant(tenantId, fromUtc, toUtc, Math.Clamp(take, 1, 1000));
            return new ApiResponses().OkResult(entries);
        }

        [Authorize(Policy = SuperAdminRequirement.PolicyName)]
        [HttpGet("Tenants/{tenantId:guid}/Payouts")]
        public async Task<IActionResult> ListTenantPayouts(Guid tenantId)
        {
            var payouts = await _payouts.ListByTenant(tenantId);
            return new ApiResponses().OkResult(payouts);
        }

        [Authorize(Policy = SuperAdminRequirement.PolicyName)]
        [HttpPost("Tenants/{tenantId:guid}/Payouts")]
        public async Task<IActionResult> CreateTenantPayout(Guid tenantId, [FromBody] CreatePayoutRequest request)
        {
            if (request.PeriodEndUtc <= request.PeriodStartUtc)
            {
                return new ApiResponses().BadRequestResult("PeriodEndUtc must be after PeriodStartUtc.");
            }
            if (!Guid.TryParse(User.FindFirst("UserId")?.Value, out var creatorId))
            {
                return new ApiResponses().BadRequestResult("Invalid token.");
            }

            var payout = new TenantPayout
            {
                TenantId = tenantId,
                Status = "pending",
                PeriodStartUtc = request.PeriodStartUtc,
                PeriodEndUtc = request.PeriodEndUtc,
                Memo = request.Memo,
                CreatedByUserId = creatorId,
            };
            payout.Id = await _payouts.Create(payout);

            var attached = await _payouts.AttachUnpaidEntries(payout.Id, tenantId, request.PeriodStartUtc, request.PeriodEndUtc);
            await _payouts.RefreshTotals(payout.Id);

            var fresh = await _payouts.GetById(payout.Id, tenantId);
            await _audit.Log("payout.create", $"Created payout for {request.PeriodStartUtc:yyyy-MM-dd} – {request.PeriodEndUtc:yyyy-MM-dd}, attached {attached} entries",
                "payout", payout.Id, tenantId, new { request.PeriodStartUtc, request.PeriodEndUtc, attached, fresh?.NetPaidCents });
            return new ApiResponses().OkResult(new { payout = fresh, attachedCount = attached });
        }

        /// <summary>
        /// Sends an existing pending payout to the tenant via Stripe Transfer (platform balance →
        /// tenant's connected Express account → tenant bank). Requires the tenant to have an
        /// active Connect account on file. Marks the payout 'processing' with the Stripe transfer
        /// id as external_reference; the transfer.paid webhook flips it to 'paid'.
        /// </summary>
        [Authorize(Policy = SuperAdminRequirement.PolicyName)]
        [HttpPost("Tenants/{tenantId:guid}/Payouts/{payoutId:guid}/SendViaStripe")]
        public async Task<IActionResult> SendPayoutViaStripe(Guid tenantId, Guid payoutId, CancellationToken ct)
        {
            var payout = await _payouts.GetById(payoutId, tenantId);
            if (payout is null) return new ApiResponses().NotFoundResult("Payout not found.");
            if (payout.Status != "pending")
            {
                return new ApiResponses().BadRequestResult($"Payout is in status '{payout.Status}'; only 'pending' payouts can be sent.");
            }
            if (payout.NetPaidCents <= 0)
            {
                return new ApiResponses().BadRequestResult("Payout net amount is zero or negative; nothing to transfer.");
            }

            var tenant = await _tenants.GetById(tenantId);
            if (tenant is null) return new ApiResponses().NotFoundResult("Tenant not found.");
            if (string.IsNullOrEmpty(tenant.StripeConnectAccountId) || tenant.StripeConnectStatus != "active")
            {
                return new ApiResponses().BadRequestResult(
                    "Tenant doesn't have an active Stripe Connect account. Have them complete onboarding before sending a Stripe payout.");
            }

            Guid? approverId = null;
            if (Guid.TryParse(User.FindFirst("UserId")?.Value, out var u)) approverId = u;

            TransferResult transfer;
            try
            {
                transfer = await _payments.CreateTransferAsync(
                    connectAccountId: tenant.StripeConnectAccountId,
                    amountCents: payout.NetPaidCents,
                    currency: "usd",
                    description: $"RidePass payout for {tenant.DisplayName} ({payout.PeriodStartUtc:yyyy-MM-dd} to {payout.PeriodEndUtc:yyyy-MM-dd})",
                    metadata: new Dictionary<string, string>
                    {
                        ["ridepass_payout_id"] = payout.Id.ToString(),
                        ["ridepass_tenant_id"] = tenantId.ToString(),
                    },
                    idempotencyKey: $"payout-{payout.Id}",
                    ct: ct);
            }
            catch (Stripe.StripeException ex)
            {
                await _audit.Log("payout.stripeTransferFailed", $"Stripe Transfer.create failed: {ex.StripeError?.Message ?? ex.Message}",
                    "payout", payoutId, tenantId, new { ex.StripeError?.Code, ex.StripeError?.Type });
                return new ApiResponses().BadRequestResult($"Stripe rejected the transfer: {ex.StripeError?.Message ?? ex.Message}");
            }

            // Stripe Transfer.create is effectively synchronous from a settlement perspective:
            // funds leave platform balance and land in the tenant's Connect balance immediately.
            // The actual bank deposit then runs on the connected account's own payout schedule,
            // which we don't need to mirror. Mark 'paid' now; the transfer.* webhook is just a
            // backstop in case Stripe reverses it later.
            await _payouts.UpdateStatus(payoutId, tenantId, "paid",
                payoutDateUtc: DateTime.UtcNow, externalReference: transfer.TransferId,
                memo: payout.Memo, approvedByUserId: approverId);

            await _audit.Log("payout.stripeTransferSent",
                $"Sent ${(payout.NetPaidCents / 100m):0.00} via Stripe Transfer {transfer.TransferId}",
                "payout", payoutId, tenantId, new { transfer.TransferId, payout.NetPaidCents });

            var amountStr = $"${(payout.NetPaidCents / 100m):0.00}";
            await _notifications.EmitToTenantAdmins(tenantId, "payout_paid",
                $"Payout sent: {amountStr}",
                $"A payout of {amountStr} for the period {payout.PeriodStartUtc:yyyy-MM-dd} – {payout.PeriodEndUtc:yyyy-MM-dd} was sent via Stripe (ref: {transfer.TransferId}).",
                "/Admin/Payouts");

            var fresh = await _payouts.GetById(payoutId, tenantId);
            return new ApiResponses().OkResult(new { payout = fresh, transferId = transfer.TransferId });
        }

        [Authorize(Policy = SuperAdminRequirement.PolicyName)]
        [HttpPut("Tenants/{tenantId:guid}/Payouts/{payoutId:guid}/Status")]
        public async Task<IActionResult> UpdateTenantPayoutStatus(Guid tenantId, Guid payoutId, [FromBody] UpdatePayoutStatusRequest request)
        {
            var allowed = new[] { "pending", "processing", "paid", "failed", "on_hold" };
            if (!allowed.Contains(request.Status))
            {
                return new ApiResponses().BadRequestResult($"Invalid status. Must be one of: {string.Join(", ", allowed)}");
            }
            if (request.Status == "paid" && request.PayoutDateUtc is null)
            {
                return new ApiResponses().BadRequestResult("PayoutDateUtc is required when marking a payout as paid.");
            }

            var existing = await _payouts.GetById(payoutId, tenantId);
            if (existing is null) return new ApiResponses().NotFoundResult("Payout not found.");

            Guid? approverId = null;
            if (request.Status == "paid" && Guid.TryParse(User.FindFirst("UserId")?.Value, out var u))
            {
                approverId = u;
            }

            await _payouts.UpdateStatus(payoutId, tenantId, request.Status, request.PayoutDateUtc, request.ExternalReference, request.Memo, approverId);
            var fresh = await _payouts.GetById(payoutId, tenantId);
            await _audit.Log("payout.statusChange", $"Payout status: {existing.Status} → {request.Status}",
                "payout", payoutId, tenantId, new { from = existing.Status, to = request.Status, request.ExternalReference, request.PayoutDateUtc });

            // Notify the tenant admins when a payout actually lands.
            if (existing.Status != "paid" && request.Status == "paid" && fresh is not null)
            {
                var amount = $"${(fresh.NetPaidCents / 100m):0.00}";
                var refSuffix = string.IsNullOrEmpty(request.ExternalReference) ? "" : $" (ref: {request.ExternalReference})";
                await _notifications.EmitToTenantAdmins(tenantId, "payout_paid",
                    $"Payout sent: {amount}",
                    $"A payout of {amount} for the period {fresh.PeriodStartUtc:yyyy-MM-dd} – {fresh.PeriodEndUtc:yyyy-MM-dd} has been sent{refSuffix}.",
                    "/Admin/Payouts");
            }

            // Notify super admins when a payout transitions into a failed state — needs investigation.
            if (existing.Status != "failed" && request.Status == "failed" && fresh is not null)
            {
                var amount = $"${(fresh.NetPaidCents / 100m):0.00}";
                await _notifications.EmitToSuperAdmins(
                    kind: "payout_failed",
                    title: $"Payout failed: {amount}",
                    body: $"Payout for tenant {tenantId} (period {fresh.PeriodStartUtc:yyyy-MM-dd} – {fresh.PeriodEndUtc:yyyy-MM-dd}) is marked failed. Investigate.",
                    linkUrl: "/SuperAdmin",
                    tenantId: tenantId);
            }

            return new ApiResponses().OkResult(fresh);
        }

        [Authorize(Policy = SuperAdminRequirement.PolicyName)]
        [HttpDelete("Tenants/{tenantId:guid}/Payouts/{payoutId:guid}")]
        public async Task<IActionResult> VoidTenantPayout(Guid tenantId, Guid payoutId)
        {
            var existing = await _payouts.GetById(payoutId, tenantId);
            if (existing is null) return new ApiResponses().NotFoundResult("Payout not found.");
            if (existing.Status != "pending")
            {
                return new ApiResponses().BadRequestResult($"Only pending payouts can be voided. This one is '{existing.Status}'.");
            }
            var ok = await _payouts.Void(payoutId, tenantId);
            if (!ok) return new ApiResponses().BadRequestResult("Could not void payout.");
            await _audit.Log("payout.void", $"Voided pending payout", "payout", payoutId, tenantId);
            return new ApiResponses().OkResult(new { id = payoutId, status = "voided" });
        }

        [Authorize(Policy = SuperAdminRequirement.PolicyName)]
        [HttpGet("Tenants/{tenantId:guid}/Payouts/{payoutId:guid}")]
        public async Task<IActionResult> GetTenantPayout(Guid tenantId, Guid payoutId)
        {
            var payout = await _payouts.GetById(payoutId, tenantId);
            if (payout is null) return new ApiResponses().NotFoundResult("Payout not found.");
            var entries = await _payouts.ListEntriesForPayout(payoutId);
            return new ApiResponses().OkResult(new { payout, entries });
        }

        [Authorize(Policy = SuperAdminRequirement.PolicyName)]
        [HttpGet("Tenants/{tenantId:guid}/Payouts/{payoutId:guid}/Csv")]
        public async Task<IActionResult> GetTenantPayoutCsv(Guid tenantId, Guid payoutId)
        {
            var payout = await _payouts.GetById(payoutId, tenantId);
            if (payout is null) return new ApiResponses().NotFoundResult("Payout not found.");
            var entries = await _payouts.ListEntriesForPayout(payoutId);
            var tenant = await _tenants.GetById(tenantId);
            var csv = PayoutCsvBuilder.Build(payout, entries, tenant?.Subdomain ?? "", tenant?.DisplayName ?? "");
            var bytes = System.Text.Encoding.UTF8.GetBytes(csv);
            return File(bytes, "text/csv", PayoutCsvBuilder.FilenameFor(payout, tenant?.Subdomain ?? ""));
        }

        [Authorize(Policy = SuperAdminRequirement.PolicyName)]
        [HttpGet("Reconciliation")]
        public async Task<IActionResult> GetReconciliation([FromQuery] DateTime fromUtc, [FromQuery] DateTime toUtc, CancellationToken ct)
        {
            if (toUtc <= fromUtc) return new ApiResponses().BadRequestResult("toUtc must be after fromUtc.");
            var stripe = await _payments.SummarizeBalanceTransactionsAsync(fromUtc, toUtc, ct);
            var ledger = await _ledger.SumForPeriod(fromUtc, toUtc);
            // Gap: Stripe net should equal (ledger gross - ledger stripe_fee). RidePass cut goes to RidePass, not Stripe.
            // So Stripe gross = ledger gross, Stripe fees = ledger stripe_fee, Stripe net = ledger gross - ledger stripe_fee.
            var grossGap   = stripe is null ? 0 : stripe.GrossCents - ledger.GrossCents;
            var feeGap     = stripe is null ? 0 : stripe.FeeCents   - ledger.StripeFeeCents;
            var expectedStripeNet = ledger.GrossCents - ledger.StripeFeeCents;
            var netGap     = stripe is null ? 0 : stripe.NetCents - expectedStripeNet;
            return new ApiResponses().OkResult(new {
                fromUtc, toUtc,
                stripe,
                ledger,
                gaps = new { grossGap, feeGap, netGap, expectedStripeNet },
                stripeConfigured = stripe is not null,
            });
        }

        [Authorize(Policy = SuperAdminRequirement.PolicyName)]
        [HttpGet("AuditLog")]
        public async Task<IActionResult> ListAuditLog(
            [FromQuery] string? action,
            [FromQuery] Guid? actorUserId,
            [FromQuery] string? targetKind,
            [FromQuery] Guid? targetId,
            [FromQuery] Guid? tenantId,
            [FromQuery] DateTime? fromUtc,
            [FromQuery] DateTime? toUtc,
            [FromQuery] int take = 200)
        {
            var entries = await _auditRepo.List(action, actorUserId, targetKind, targetId, tenantId, fromUtc, toUtc, Math.Clamp(take, 1, 1000));
            return new ApiResponses().OkResult(entries);
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
            ServiceChargeBps = t.ServiceChargeBps,
            MonthlyServiceChargeCapCents = t.MonthlyServiceChargeCapCents,
            IsPublished = t.IsPublished,
            ConcessionsEnabled = t.ConcessionsEnabled,
            AddressLine = t.AddressLine,
            City = t.City,
            Region = t.Region,
            PostalCode = t.PostalCode,
            Country = t.Country,
            Latitude = t.Latitude,
            Longitude = t.Longitude,
            ContactEmail = t.ContactEmail,
            Phone = t.Phone,
            LoampassMxDestinationId = t.LoampassMxDestinationId,
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

        // ── Marketing capture: recipient emails harvested via coupon shares ─────
        // Each row = one rider sending a coupon to a friend. Useful for tenant outreach
        // ("hey, your friend sent you a code last month — come try a pass?").

        [Authorize(Policy = SuperAdminRequirement.PolicyName)]
        [HttpGet("Marketing/CouponShares")]
        public async Task<IActionResult> ListCouponShares([FromQuery] Guid? tenantId)
        {
            var tenants = tenantId.HasValue
                ? new List<Tenant?> { await _tenants.GetById(tenantId.Value) }.Where(t => t is not null).Cast<Tenant>().ToList()
                : await _tenants.ListAll();

            var rows = new List<object>();
            foreach (var t in tenants)
            {
                var shares = await _couponShares.ListSharesByTenant(t.Id, take: 10000);
                foreach (var s in shares)
                {
                    rows.Add(new
                    {
                        tenantSubdomain = t.Subdomain,
                        tenantDisplayName = t.DisplayName,
                        recipientEmail = s.RecipientEmail,
                        recipientName = s.RecipientName,
                        sentAtUtc = DateTime.SpecifyKind(s.SentAt, DateTimeKind.Utc),
                        redeemedAtUtc = s.RedeemedAt is null ? (DateTime?)null : DateTime.SpecifyKind(s.RedeemedAt.Value, DateTimeKind.Utc),
                    });
                }
            }
            return new ApiResponses().OkResult(rows);
        }
    }
}
