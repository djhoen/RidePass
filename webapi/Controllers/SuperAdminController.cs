using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
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
        private readonly IDayPassPurchaseRepository _dayPasses;
        private readonly IEventTicketPurchaseRepository _tickets;
        private readonly IDisputeRepository _disputes;
        private readonly IReportsRepository _reports;
        private readonly IPaymentProvider _payments;
        private readonly IPasswordHasher<User> _passwordHasher;
        private readonly IJwtIssuer _jwtIssuer;
        private readonly IFeeScheduleRepository _feeSchedules;
        private readonly ITenantLedgerRepository _ledger;
        private readonly ITenantPayoutRepository _payouts;
        private readonly INotificationService _notifications;
        private readonly IAuditLogger _audit;
        private readonly IAuditLogRepository _auditRepo;

        public SuperAdminController(
            IUserRepository users,
            ITenantRepository tenants,
            IDayPassPurchaseRepository dayPasses,
            IEventTicketPurchaseRepository tickets,
            IDisputeRepository disputes,
            IReportsRepository reports,
            IPaymentProvider payments,
            IPasswordHasher<User> passwordHasher,
            IJwtIssuer jwtIssuer,
            IFeeScheduleRepository feeSchedules,
            ITenantLedgerRepository ledger,
            ITenantPayoutRepository payouts,
            INotificationService notifications,
            IAuditLogger audit,
            IAuditLogRepository auditRepo)
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
            _feeSchedules = feeSchedules;
            _ledger = ledger;
            _payouts = payouts;
            _notifications = notifications;
            _audit = audit;
            _auditRepo = auditRepo;
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
                Timezone = request.Timezone,
            };
            tenant.Id = await _tenants.Create(tenant);
            await _audit.Log("tenant.create", $"Created tenant '{tenant.DisplayName}' ({tenant.Subdomain})",
                "tenant", tenant.Id, tenant.Id, new { tenant.Subdomain, tenant.DisplayName, tenant.Timezone });

            // Default fee schedule: flat 5%, no monthly cap. Super admin can edit per-tenant later.
            await _feeSchedules.Replace(
                new TenantFeeSchedule
                {
                    TenantId = tenant.Id,
                    EffectiveFromUtc = DateTime.UtcNow,
                    MonthlyCapCents = null,
                },
                new[]
                {
                    new TenantFeeTier { MinVolumeCents = 0, MaxVolumeCents = null, RateBps = 500, SortOrder = 1 }
                });

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
                await WriteRefundLedgerEntry(purchase.TenantId, "day_pass", id, refund.RefundId);
                var amount = $"${(purchase.AmountCents / 100m):0.00}";
                await _audit.Log("refund.process", $"Refunded day pass {amount} for {purchase.PurchaserEmail}",
                    "day_pass_purchase", id, purchase.TenantId, new { refund.RefundId, purchase.AmountCents });
                await _notifications.EmitToTenantAdmins(purchase.TenantId, "refund_processed",
                    $"Refund issued: {amount}",
                    $"A {amount} refund was issued for {purchase.PurchaserName} ({purchase.PurchaserEmail}).",
                    "/Admin/Purchases");
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
                await WriteRefundLedgerEntry(purchase.TenantId, "event_ticket", id, refund.RefundId);
                var amount = $"${(purchase.AmountCents / 100m):0.00}";
                await _audit.Log("refund.process", $"Refunded event ticket {amount} for {purchase.PurchaserEmail}",
                    "event_ticket_purchase", id, purchase.TenantId, new { refund.RefundId, purchase.AmountCents });
                await _notifications.EmitToTenantAdmins(purchase.TenantId, "refund_processed",
                    $"Refund issued: {amount}",
                    $"A {amount} refund was issued for {purchase.PurchaserName} ({purchase.PurchaserEmail}).",
                    "/Admin/Purchases");
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult($"Stripe refund failed: {ex.Message}");
            }
            return new ApiResponses().OkResult(new { id, status = "refunded" });
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
        [HttpGet("Balances")]
        public async Task<IActionResult> ListTenantBalances()
        {
            var summaries = await _ledger.GetSummariesForAllTenants();
            return new ApiResponses().OkResult(summaries);
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
        [HttpGet("Tenants/{tenantId:guid}/FeeSchedule")]
        public async Task<IActionResult> GetTenantFeeSchedule(Guid tenantId)
        {
            var schedule = await _feeSchedules.GetActive(tenantId, DateTime.UtcNow);
            if (schedule is null) return new ApiResponses().NotFoundResult("No active fee schedule.");
            return new ApiResponses().OkResult(schedule);
        }

        [Authorize(Policy = SuperAdminRequirement.PolicyName)]
        [HttpPut("Tenants/{tenantId:guid}/FeeSchedule")]
        public async Task<IActionResult> UpdateTenantFeeSchedule(Guid tenantId, [FromBody] UpdateFeeScheduleRequest request)
        {
            // Validate tiers: must start at 0, no overlaps, ascending min, exactly one open-ended top tier.
            var sorted = request.Tiers.OrderBy(t => t.MinVolumeCents).ToList();
            if (sorted[0].MinVolumeCents != 0)
            {
                return new ApiResponses().BadRequestResult("First tier must start at 0.");
            }
            for (var i = 0; i < sorted.Count - 1; i++)
            {
                if (sorted[i].MaxVolumeCents is null)
                {
                    return new ApiResponses().BadRequestResult("Only the last tier may have no max.");
                }
                if (sorted[i].MaxVolumeCents != sorted[i + 1].MinVolumeCents)
                {
                    return new ApiResponses().BadRequestResult($"Tier {i + 1}'s max must equal tier {i + 2}'s min (no gaps or overlaps).");
                }
            }

            var schedule = new TenantFeeSchedule
            {
                TenantId = tenantId,
                EffectiveFromUtc = DateTime.UtcNow,
                MonthlyCapCents = request.MonthlyCapCents,
            };
            var tiers = sorted.Select((t, idx) => new TenantFeeTier
            {
                MinVolumeCents = t.MinVolumeCents,
                MaxVolumeCents = t.MaxVolumeCents,
                RateBps = t.RateBps,
                SortOrder = idx + 1,
            });
            await _feeSchedules.Replace(schedule, tiers);
            await _audit.Log("fee_schedule.update", $"Updated fee schedule ({sorted.Count} tier(s), cap={request.MonthlyCapCents})",
                "tenant", tenantId, tenantId, new { request.MonthlyCapCents, Tiers = sorted });

            var newSchedule = await _feeSchedules.GetActive(tenantId, DateTime.UtcNow);
            return new ApiResponses().OkResult(newSchedule);
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
