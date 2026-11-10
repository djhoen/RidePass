using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Coupons;
using Services.Helpers;
using Services.Repositories.Data.VoucherData;
using Services.Repositories.Interfaces;
using Services.Vouchers;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Voucher;
using webapi.Helpers;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    /// <summary>
    /// Vouchers (Script0315, Highland go-live item 11). A voucher is a code good for one free rider
    /// admission, redeemed onto a rider profile at online checkout or at Gate Sale, after which it is an
    /// ordinary ticket with the ordinary check-in.
    ///
    /// * Any staff member gives codes to friends from a yearly allowance an Admin sets ("Mine").
    ///   Admins are unlimited.
    /// * Admins mint vendor lists ("10 free passes for Red Bull") and see every staff member's use.
    /// </summary>
    [ApiController]
    [Route("api/Voucher")]
    [Authorize]
    public class VoucherController : ControllerBase
    {
        private const string CodePrefix = "VCH";

        // Everyone at a venue except riders may hold an allowance.
        private static readonly HashSet<string> StaffRoles = new()
        {
            "tenant_admin", "tenant_manager", "tenant_accountant", "tenant_cashier",
            "tenant_fnb_cashier", "tenant_shop_cashier", "tenant_scanner", "tenant_staff",
        };

        private readonly IVoucherRepository _vouchers;
        private readonly IEventRepository _events;
        private readonly IUserRepository _users;
        private readonly ITenantRepository _tenants;
        private readonly IEmailSuppressionRepository _suppression;
        private readonly ISmtpEmailer _emailer;
        private readonly Services.Helpers.Interfaces.IDbHelper _db;
        private readonly Services.Audit.IAuditLogger _audit;
        private readonly IConfiguration _config;
        private readonly ILogger<VoucherController> _logger;
        private readonly ITenantContext _tenantContext;

        public VoucherController(IVoucherRepository vouchers, IEventRepository events, IUserRepository users,
            ITenantRepository tenants, IEmailSuppressionRepository suppression, ISmtpEmailer emailer,
            Services.Helpers.Interfaces.IDbHelper db, Services.Audit.IAuditLogger audit, IConfiguration config,
            ILogger<VoucherController> logger, ITenantContext tenantContext)
        {
            _vouchers = vouchers;
            _events = events;
            _users = users;
            _tenants = tenants;
            _suppression = suppression;
            _emailer = emailer;
            _db = db;
            _audit = audit;
            _config = config;
            _logger = logger;
            _tenantContext = tenantContext;
        }

        // ── Mine: any staff member ──────────────────────────────────────────────

        [HttpGet("Mine")]
        public async Task<IActionResult> Mine()
        {
            if (Caller() is not { } me) return Forbid();
            var (unlimited, allowance, issued, remaining) = await AllowanceFor(me.UserId, me.IsAdmin);
            var batches = await _vouchers.ListBatches(_tenantContext.TenantId, me.UserId, VoucherKinds.Employee, 200);
            return new ApiResponses().OkResult(new MyVouchersResponse
            {
                Unlimited = unlimited,
                Remaining = remaining,
                YearlyAllowance = allowance,
                IssuedThisYear = issued,
                Year = TenantClock.LocalDate(_tenantContext.Tenant.Timezone, DateTime.UtcNow).Year,
                CanManage = me.IsAdmin,
                Batches = batches.Select(ToItem).ToList(),
            });
        }

        [HttpPost("Mine")]
        public async Task<IActionResult> IssueMine([FromBody] IssueVouchersRequest request)
        {
            if (Caller() is not { } me) return Forbid();
            if (request.Count < 1 || request.Count > VoucherRules.MaxCodesPerEmployeeGift)
                return new ApiResponses().BadRequestResult($"Give 1 to {VoucherRules.MaxCodesPerEmployeeGift} vouchers at a time.");
            if (string.IsNullOrWhiteSpace(request.RecipientName))
                return new ApiResponses().BadRequestResult("Say who the vouchers are for.");
            if (ExpiryUtc(request.ExpiresOn, out var expiresAtUtc) is { } expiryError)
                return new ApiResponses().BadRequestResult(expiryError);

            // One giver at a time, so two tabs can't both spend the last voucher on the allowance.
            await using var allowanceLock = await _db.AcquireAdvisoryLock($"voucher-allowance:{_tenantContext.TenantId}:{me.UserId}");
            var (_, _, _, remaining) = await AllowanceFor(me.UserId, me.IsAdmin);
            if (remaining is int left && request.Count > left)
            {
                return new ApiResponses().BadRequestResult(left == 0
                    ? "You have no vouchers left this year. Ask an Admin for more."
                    : $"You have {left} voucher{(left == 1 ? "" : "s")} left this year.");
            }

            var giverName = await NameOf(me.UserId);
            var (batch, codes) = await Mint(new VoucherBatch
            {
                TenantId = _tenantContext.TenantId,
                Kind = VoucherKinds.Employee,
                IssuedByUserId = me.UserId,
                Label = request.RecipientName.Trim(),
                RecipientEmail = NullIfBlank(request.RecipientEmail),
                Note = NullIfBlank(request.Note),
                CodeCount = request.Count,
                ExpiresAtUtc = expiresAtUtc,
            }, $"Voucher from {giverName}");
            await allowanceLock.DisposeAsync();

            await _audit.Log("voucher.issue", $"Gave {codes.Count} voucher{(codes.Count == 1 ? "" : "s")} to {batch.Label}",
                targetKind: "voucher_batch", targetId: batch.Id, tenantId: _tenantContext.TenantId,
                metadata: new { kind = batch.Kind, count = codes.Count, recipientEmail = batch.RecipientEmail });
            var emailed = await SendCodes(batch, codes, giverName);
            return new ApiResponses().OkResult(await IssueResponse(batch, codes, emailed));
        }

        // ── Codes of one batch: its issuer, or an Admin ─────────────────────────

        [HttpGet("Batches/{id:guid}/Codes")]
        public async Task<IActionResult> Codes(Guid id)
        {
            if (Caller() is not { } me) return Forbid();
            var batch = await _vouchers.GetBatch(_tenantContext.TenantId, id);
            if (batch is null) return new ApiResponses().NotFoundResult("Voucher batch not found.");
            if (!me.IsAdmin && batch.IssuedByUserId != me.UserId) return Forbid();
            var rows = await _vouchers.ListCodes(_tenantContext.TenantId, id);
            return new ApiResponses().OkResult(rows.Select(ToCodeItem).ToList());
        }

        [HttpPost("Batches/{id:guid}/Void")]
        public async Task<IActionResult> Void(Guid id, [FromBody] VoidVouchersRequest request)
        {
            if (Caller() is not { } me) return Forbid();
            var batch = await _vouchers.GetBatch(_tenantContext.TenantId, id);
            if (batch is null) return new ApiResponses().NotFoundResult("Voucher batch not found.");
            // A giver may take back their own gift (the codes return to their allowance); vendor
            // lists are an Admin's.
            if (!me.IsAdmin && !(batch.Kind == VoucherKinds.Employee && batch.IssuedByUserId == me.UserId)) return Forbid();
            var voided = await _vouchers.VoidUnusedCodes(_tenantContext.TenantId, id, request.CouponId);
            if (voided == 0)
                return new ApiResponses().BadRequestResult(request.CouponId is null
                    ? "Every code in this batch is already used or voided."
                    : "That code is already used or voided.");
            await _audit.Log("voucher.void", $"Voided {voided} unused voucher{(voided == 1 ? "" : "s")} ({batch.Label})",
                targetKind: "voucher_batch", targetId: batch.Id, tenantId: _tenantContext.TenantId,
                metadata: new { couponId = request.CouponId, voided });
            return new ApiResponses().OkResult(new { voided });
        }

        // ── Admin: staff allowances, every batch, vendor lists ──────────────────

        [Authorize(Policy = TenantAdminRequirement.PolicyName)]
        [HttpGet("Staff")]
        public async Task<IActionResult> Staff()
        {
            var since = VoucherRules.YearStartUtc(_tenantContext.Tenant.Timezone, DateTime.UtcNow);
            var rows = await _vouchers.ListStaffSummary(_tenantContext.TenantId, since);
            return new ApiResponses().OkResult(rows.Select(r =>
            {
                var isAdmin = r.Role == "tenant_admin" || r.Roles.Contains("tenant_admin");
                var remaining = VoucherRules.Remaining(isAdmin, r.HasAllowanceRow, r.YearlyAllowance, r.IssuedThisYear);
                return new StaffVoucherItem
                {
                    UserId = r.UserId,
                    Name = $"{r.FirstName} {r.LastName}".Trim(),
                    Email = r.Email,
                    IsAdmin = isAdmin,
                    Unlimited = remaining is null,
                    YearlyAllowance = r.HasAllowanceRow ? r.YearlyAllowance : (isAdmin ? null : 0),
                    IssuedThisYear = r.IssuedThisYear,
                    RedeemedThisYear = r.RedeemedThisYear,
                    Remaining = remaining,
                };
            }).ToList());
        }

        [Authorize(Policy = TenantAdminRequirement.PolicyName)]
        [HttpPut("Staff/{userId:guid}/Allowance")]
        public async Task<IActionResult> SetAllowance(Guid userId, [FromBody] SetVoucherAllowanceRequest request)
        {
            if (Caller() is not { } me) return Forbid();
            var user = await _users.GetById(userId);
            if (user is null || user.TenantId != _tenantContext.TenantId)
                return new ApiResponses().NotFoundResult("Staff member not found.");
            var roles = user.Roles is { Length: > 0 } ? user.Roles : new[] { user.Role };
            if (!roles.Any(StaffRoles.Contains))
                return new ApiResponses().BadRequestResult("Only staff can hold a voucher allowance.");
            if (!request.Unlimited && request.YearlyAllowance is null or < 0 or > 1000)
                return new ApiResponses().BadRequestResult("Give an allowance from 0 to 1000 a year, or make it unlimited.");
            int? allowance = request.Unlimited ? null : request.YearlyAllowance;
            await _vouchers.SetAllowance(_tenantContext.TenantId, userId, allowance, me.UserId);
            await _audit.Log("voucher.allowance",
                $"Set {user.FirstName} {user.LastName}'s voucher allowance to {(allowance is null ? "unlimited" : $"{allowance} a year")}",
                targetKind: "user", targetId: userId, tenantId: _tenantContext.TenantId,
                metadata: new { yearlyAllowance = allowance });
            return new ApiResponses().OkResult(new { userId, unlimited = allowance is null, yearlyAllowance = allowance });
        }

        [Authorize(Policy = TenantAdminRequirement.PolicyName)]
        [HttpGet("Batches")]
        public async Task<IActionResult> Batches([FromQuery] string? kind, [FromQuery] Guid? issuedBy)
        {
            if (kind is not null && kind != VoucherKinds.Employee && kind != VoucherKinds.Vendor)
                return new ApiResponses().BadRequestResult("kind is employee or vendor.");
            var rows = await _vouchers.ListBatches(_tenantContext.TenantId, issuedBy, kind, 500);
            return new ApiResponses().OkResult(rows.Select(ToItem).ToList());
        }

        [Authorize(Policy = TenantAdminRequirement.PolicyName)]
        [HttpPost("Vendor")]
        public async Task<IActionResult> CreateVendor([FromBody] CreateVendorVouchersRequest request)
        {
            if (Caller() is not { } me) return Forbid();
            if (request.Count < 1 || request.Count > VoucherRules.MaxCodesPerBatch)
                return new ApiResponses().BadRequestResult($"Make 1 to {VoucherRules.MaxCodesPerBatch} codes at a time.");
            if (string.IsNullOrWhiteSpace(request.Label))
                return new ApiResponses().BadRequestResult("Name who the codes are for (e.g. Red Bull).");
            if (ExpiryUtc(request.ExpiresOn, out var expiresAtUtc) is { } expiryError)
                return new ApiResponses().BadRequestResult(expiryError);
            if (request.EventId is Guid eventId && await _events.GetById(eventId, _tenantContext.TenantId) is null)
                return new ApiResponses().BadRequestResult("That event isn't one of this venue's.");

            var (batch, codes) = await Mint(new VoucherBatch
            {
                TenantId = _tenantContext.TenantId,
                Kind = VoucherKinds.Vendor,
                IssuedByUserId = me.UserId,
                Label = request.Label.Trim(),
                RecipientEmail = NullIfBlank(request.RecipientEmail),
                Note = NullIfBlank(request.Note),
                CodeCount = request.Count,
                EventId = request.EventId,
                ExpiresAtUtc = expiresAtUtc,
            }, $"Voucher for {request.Label.Trim()}");

            await _audit.Log("voucher.vendor", $"Made {codes.Count} voucher codes for {batch.Label}",
                targetKind: "voucher_batch", targetId: batch.Id, tenantId: _tenantContext.TenantId,
                metadata: new { count = codes.Count, eventId = batch.EventId, recipientEmail = batch.RecipientEmail });
            var emailed = await SendCodes(batch, codes, _tenantContext.Tenant.DisplayName);
            return new ApiResponses().OkResult(await IssueResponse(batch, codes, emailed));
        }

        [HttpGet("Batches/{id:guid}/Codes.csv")]
        public async Task<IActionResult> CodesCsv(Guid id)
        {
            if (Caller() is not { } me) return Forbid();
            var batch = await _vouchers.GetBatch(_tenantContext.TenantId, id);
            if (batch is null) return new ApiResponses().NotFoundResult("Voucher batch not found.");
            if (!me.IsAdmin && batch.IssuedByUserId != me.UserId) return Forbid();
            var rows = await _vouchers.ListCodes(_tenantContext.TenantId, id);
            var sb = new System.Text.StringBuilder("Code,Status,Expires,Redeemed at,Redeemed by,Rider,Event\r\n");
            foreach (var r in rows.Select(ToCodeItem))
            {
                sb.Append(string.Join(",", new[]
                {
                    r.Code, r.Status, r.ExpiresAtUtc?.ToString("yyyy-MM-dd") ?? "",
                    r.RedeemedAtUtc?.ToString("yyyy-MM-dd HH:mm") ?? "", r.RedeemedByName ?? "", r.RiderName ?? "", r.EventTitle ?? "",
                }.Select(Csv)));
                sb.Append("\r\n");
            }
            var name = new string((batch.Label ?? "vouchers").Where(char.IsLetterOrDigit).ToArray());
            return File(System.Text.Encoding.UTF8.GetBytes(sb.ToString()), "text/csv",
                $"vouchers-{(name.Length > 0 ? name : "batch")}.csv");
        }

        // ── helpers ─────────────────────────────────────────────────────────────

        private sealed record CallerInfo(Guid UserId, bool IsAdmin);

        // The giver's name, for the email and the code description (the token carries no name).
        private async Task<string> NameOf(Guid userId)
        {
            var u = await _users.GetById(userId);
            var name = u is null ? "" : $"{u.FirstName} {u.LastName}".Trim();
            return name.Length > 0 ? name : "A friend";
        }

        // Staff of this venue (or a platform super admin acting for it), else null.
        private CallerInfo? Caller()
        {
            if (!_tenantContext.IsResolved) return null;
            if (!Guid.TryParse(User.FindFirst("UserId")?.Value, out var userId)) return null;
            var roles = User.FindAll("role").Select(c => c.Value).ToList();
            if (roles.Contains("super_admin")) return new CallerInfo(userId, true);
            if (!Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var claimTenant) || claimTenant != _tenantContext.TenantId)
                return null;
            if (!roles.Any(StaffRoles.Contains)) return null;
            return new CallerInfo(userId, roles.Contains("tenant_admin"));
        }

        private async Task<(bool Unlimited, int? Allowance, int Issued, int? Remaining)> AllowanceFor(Guid userId, bool isAdmin)
        {
            var since = VoucherRules.YearStartUtc(_tenantContext.Tenant.Timezone, DateTime.UtcNow);
            var row = await _vouchers.GetAllowance(_tenantContext.TenantId, userId);
            var issued = await _vouchers.CountIssuedSince(_tenantContext.TenantId, userId, since);
            var remaining = VoucherRules.Remaining(isAdmin, row is not null, row?.YearlyAllowance, issued);
            return (remaining is null, row is not null ? row.YearlyAllowance : (isAdmin ? null : 0), issued, remaining);
        }

        private async Task<(VoucherBatch Batch, List<string> Codes)> Mint(VoucherBatch batch, string description)
        {
            // 30^8 codes: a clash is near impossible, but a tenant's codes are unique, so check and
            // redraw rather than let the insert fail.
            for (var attempt = 0; ; attempt++)
            {
                var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                while (codes.Count < batch.CodeCount) codes.Add(CouponCodeGenerator.Generate(CodePrefix));
                var list = codes.ToList();
                if (attempt < 4 && await _vouchers.AnyCodeExists(_tenantContext.TenantId, list)) continue;
                var created = await _vouchers.CreateBatch(batch, list, description);
                return (created, list);
            }
        }

        private string? ExpiryUtc(DateOnly? expiresOn, out DateTime? expiresAtUtc)
        {
            expiresAtUtc = null;
            if (expiresOn is not DateOnly day) return null;
            var today = TenantClock.LocalDate(_tenantContext.Tenant.Timezone, DateTime.UtcNow);
            if (day < today) return "The last day can't be in the past.";
            // Good through the end of that day at the venue.
            expiresAtUtc = TenantClock.DayRangeUtc(_tenantContext.Tenant.Timezone, day).EndUtc;
            return null;
        }

        private async Task<IssueVouchersResponse> IssueResponse(VoucherBatch batch, List<string> codes, bool emailed)
        {
            var row = (await _vouchers.ListBatches(_tenantContext.TenantId, batch.IssuedByUserId, batch.Kind, 50))
                .FirstOrDefault(b => b.Id == batch.Id);
            return new IssueVouchersResponse
            {
                Batch = row is not null ? ToItem(row) : new VoucherBatchItem { Id = batch.Id, Kind = batch.Kind, CodeCount = batch.CodeCount },
                Codes = codes.OrderBy(c => c).ToList(),
                Emailed = emailed,
            };
        }

        private async Task<bool> SendCodes(VoucherBatch batch, List<string> codes, string fromName)
        {
            if (batch.RecipientEmail is not { } to || !_emailer.IsConfigured) return false;
            try
            {
                if (await _suppression.IsSuppressed(to, _tenantContext.TenantId, marketing: false)) return false;
                var tenant = await _tenants.GetById(_tenantContext.TenantId);
                if (tenant is null) return false;
                var apex = _config["App:RootDomain"] ?? "ridepass.io";
                var site = $"https://{tenant.Subdomain}.{apex}";
                var enc = (string? s) => System.Net.WebUtility.HtmlEncode(s ?? "");
                var plural = codes.Count == 1 ? "a free day" : $"{codes.Count} free days";
                var list = string.Join("", codes.OrderBy(c => c).Select(c =>
                    $"<li style=\"font-family:monospace;font-size:1.2em;font-weight:bold;margin:4px 0\">{enc(c)}</li>"));
                var note = string.IsNullOrWhiteSpace(batch.Note) ? "" :
                    $"<blockquote style=\"border-left:3px solid #ccc;padding-left:1em;color:#555\">{enc(batch.Note)}</blockquote>";
                var expiry = batch.ExpiresAtUtc is DateTime exp
                    ? $"<p style=\"color:#888\">Good through {TimeZoneInfo.ConvertTimeFromUtc(exp.AddMinutes(-1), TenantClock.Resolve(tenant.Timezone)):MMMM d, yyyy}.</p>"
                    : "";
                var html = $@"<p>Hi {enc(batch.Label)},</p>
<p><strong>{enc(fromName)}</strong> sent you {plural} at <strong>{enc(tenant.DisplayName)}</strong>.</p>
{note}
<p>Each code below admits one rider once. Use it at checkout on <a href=""{site}"">{enc(site)}</a>, or show it at the gate. Every rider needs their own profile and a signed waiver.</p>
<ul style=""list-style:none;padding:0"">{list}</ul>
{expiry}";
                return await _emailer.Send(to, $"{fromName} sent you {plural} at {tenant.DisplayName}", html, null,
                    Services.Email.TenantEmailIdentity.For(tenant));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to email voucher batch {BatchId}", batch.Id);
                return false;
            }
        }

        private static VoucherBatchItem ToItem(VoucherBatchRow b) => new()
        {
            Id = b.Id,
            Kind = b.Kind,
            IssuedByUserId = b.IssuedByUserId,
            IssuedByName = b.IssuedByName,
            Label = b.Label,
            RecipientEmail = b.RecipientEmail,
            Note = b.Note,
            CodeCount = b.CodeCount,
            RedeemedCount = b.RedeemedCount,
            VoidedCount = b.VoidedCount,
            EventId = b.EventId,
            EventTitle = b.EventTitle,
            ExpiresAtUtc = b.ExpiresAtUtc is DateTime e ? DateTime.SpecifyKind(e, DateTimeKind.Utc) : null,
            CreatedAtUtc = DateTime.SpecifyKind(b.CreatedAt, DateTimeKind.Utc),
        };

        private static VoucherCodeItem ToCodeItem(VoucherCodeRow r)
        {
            var expires = r.ValidToUtc is DateTime v ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : (DateTime?)null;
            var status = r.RedeemedAt is not null ? "redeemed"
                : r.VoidedAt is not null ? "voided"
                : expires is DateTime x && x < DateTime.UtcNow ? "expired"
                : "unused";
            return new VoucherCodeItem
            {
                Id = r.Id,
                Code = r.Code,
                Status = status,
                ExpiresAtUtc = expires,
                RedeemedAtUtc = r.RedeemedAt is DateTime ra ? DateTime.SpecifyKind(ra, DateTimeKind.Utc) : null,
                RedeemedByUserId = r.RedeemedByUserId,
                RedeemedByName = r.RedeemedByName,
                RiderName = r.RedeemedAt is not null ? r.RiderName : null,
                EventTitle = r.EventTitle,
            };
        }

        private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        private static string Csv(string s)
        {
            // A name typed by staff could start a spreadsheet formula; a leading quote keeps it text.
            if (s.Length > 0 && "=+-@\t\r".IndexOf(s[0]) >= 0) s = "'" + s;
            return s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }
    }
}
