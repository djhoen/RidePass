using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Waiver;
using webapi.Multitenancy;
using webapi.Helpers;

namespace webapi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WaiverController : ControllerBase
    {
        private readonly IWaiverRepository _repo;
        private readonly IEventRepository _events;
        private readonly IUserRepository _users;
        private readonly ITenantContext _tenantContext;
        private readonly IWaiverSignRequestRepository _signRequests;
        private readonly Services.Helpers.ISmtpEmailer _emailer;
        private readonly IConfiguration _config;

        public WaiverController(
            IWaiverRepository repo,
            IEventRepository events,
            IUserRepository users,
            ITenantContext tenantContext,
            IWaiverSignRequestRepository signRequests,
            Services.Helpers.ISmtpEmailer emailer,
            IConfiguration config)
        {
            _repo = repo;
            _events = events;
            _users = users;
            _tenantContext = tenantContext;
            _signRequests = signRequests;
            _emailer = emailer;
            _config = config;
        }

        [HttpGet]
        public async Task<IActionResult> GetActive()
        {
            if (!_tenantContext.IsResolved)
            {
                return new ApiResponses().BadRequestResult("No tenant resolved for this request.");
            }

            var waiver = await _repo.GetActive(_tenantContext.TenantId);
            if (waiver is null)
            {
                return new ApiResponses().NotFoundResult("No active waiver for this tenant.");
            }

            return new ApiResponses().OkResult(ToResponse(waiver));
        }

        // Single waiver lookup — used by the Buy Race Entry flow when an event pins
        // a specific waiver and we need to fetch its body for inline display.
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var waiver = await _repo.GetById(id, _tenantContext.TenantId);
            if (waiver is null) return new ApiResponses().NotFoundResult("Waiver not found.");
            return new ApiResponses().OkResult(ToResponse(waiver));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpGet("Admin")]
        public async Task<IActionResult> ListAdmin()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var rows = await _repo.ListByTenant(_tenantContext.TenantId);
            return new ApiResponses().OkResult(rows.Select(ToResponse));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] UpsertWaiverRequest request)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var waiver = await _repo.Create(
                _tenantContext.TenantId,
                request.Name.Trim(),
                request.Title.Trim(),
                request.Body ?? string.Empty,
                request.IsActive,
                request.ExpiresAtUtc?.ToUniversalTime());
            return new ApiResponses().OkResult(ToResponse(waiver));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpsertWaiverRequest request)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var existing = await _repo.GetById(id, _tenantContext.TenantId);
            if (existing is null) return new ApiResponses().NotFoundResult("Waiver not found.");
            await _repo.Update(id, _tenantContext.TenantId,
                request.Name.Trim(),
                request.Title.Trim(),
                request.Body ?? string.Empty,
                request.IsActive,
                request.ExpiresAtUtc?.ToUniversalTime());
            var refreshed = await _repo.GetById(id, _tenantContext.TenantId);
            return new ApiResponses().OkResult(ToResponse(refreshed!));
        }

        // Events this waiver currently fills as the rider or spectator slot. Used
        // by the waiver Edit dialog's "Associated Events" tab so the admin can see
        // — and toggle — which events reference this waiver without opening each
        // event individually.
        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpGet("{id:guid}/Events")]
        public async Task<IActionResult> ListAssociatedEvents(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var waiver = await _repo.GetById(id, _tenantContext.TenantId);
            if (waiver is null) return new ApiResponses().NotFoundResult("Waiver not found.");
            var rows = await _events.ListByWaiverId(id, _tenantContext.TenantId);
            var responses = rows.Select(r => new WaiverEventAssociationResponse
            {
                Id = r.Id,
                Title = r.Title,
                StartsAtUtc = DateTime.SpecifyKind(r.StartsAt, DateTimeKind.Utc),
                EndsAtUtc = DateTime.SpecifyKind(r.EndsAt, DateTimeKind.Utc),
                AsRider = r.AsRider,
                AsSpectator = r.AsSpectator,
            });
            return new ApiResponses().OkResult(responses);
        }

        // Attach / detach this waiver from one event for the rider and/or spectator
        // role. Passing {asRider: false, asSpectator: false} fully detaches.
        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPut("{id:guid}/Events/{eventId:guid}")]
        public async Task<IActionResult> SetAssociatedEventRole(Guid id, Guid eventId,
            [FromBody] SetWaiverEventRoleRequest request)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var waiver = await _repo.GetById(id, _tenantContext.TenantId);
            if (waiver is null) return new ApiResponses().NotFoundResult("Waiver not found.");
            var ev = await _events.GetById(eventId, _tenantContext.TenantId);
            if (ev is null) return new ApiResponses().NotFoundResult("Event not found.");
            await _events.SetWaiverRole(eventId, _tenantContext.TenantId, id,
                request.AsRider, request.AsSpectator);
            return new ApiResponses().OkResult();
        }

        // Legacy single-waiver endpoint — kept so existing PUT /Waiver clients still
        // work. Now creates a fresh waiver instead of flipping the old one inactive.
        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPut]
        public async Task<IActionResult> PublishNew([FromBody] UpdateWaiverRequest request)
        {
            var newWaiver = await _repo.PublishNewVersion(_tenantContext.TenantId, request.Title, request.Body);
            return new ApiResponses().OkResult(ToResponse(newWaiver));
        }

        private static WaiverResponse ToResponse(TenantWaiver w) => new()
        {
            Id = w.Id,
            Version = w.Version,
            Name = w.Name,
            Title = w.Title,
            Body = w.Body,
            IsActive = w.IsActive,
            ExpiresAtUtc = w.ExpiresAt.HasValue
                ? DateTime.SpecifyKind(w.ExpiresAt.Value, DateTimeKind.Utc)
                : null,
        };

        [Authorize]
        [HttpGet("MySignature")]
        public async Task<IActionResult> GetMySignatureStatus()
        {
            if (!_tenantContext.IsResolved)
            {
                return new ApiResponses().BadRequestResult("No tenant resolved.");
            }

            var active = await _repo.GetActive(_tenantContext.TenantId);
            if (active is null)
            {
                return new ApiResponses().NotFoundResult("No active waiver for this tenant.");
            }

            if (!TryGetUserId(out var userId))
            {
                return new ApiResponses().BadRequestResult("Invalid token.");
            }

            var sig = await _repo.GetSignature(userId, active.Id);
            var user = await _users.GetById(userId);
            return new ApiResponses().OkResult(new WaiverSignatureStatusResponse
            {
                CurrentVersion = active.Version,
                HasSignedCurrent = sig is not null,
                SignatureId = sig?.Id,
                SignedAt = sig?.SignedAt,
                SignatureDataUrl = sig?.SignatureDataUrl,
                RiderIsMinor = WaiverPolicy.IsMinor(user?.Birthdate),
                SignedByParent = sig?.SignedByParent ?? false,
                ParentName = sig?.ParentName,
                ParentPhone = sig?.ParentPhone,
                RiderHasEmergencyContact = !string.IsNullOrWhiteSpace(user?.EmergencyContactPhone),
            });
        }

        // Per-waiver signature lookup — used by the Buy Race Entry flow once it
        // knows which waiver applies to the chosen event.
        [Authorize]
        [HttpGet("{id:guid}/MySignature")]
        public async Task<IActionResult> GetMySignatureForWaiver(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!TryGetUserId(out var userId)) return new ApiResponses().BadRequestResult("Invalid token.");
            var waiver = await _repo.GetById(id, _tenantContext.TenantId);
            if (waiver is null) return new ApiResponses().NotFoundResult("Waiver not found.");
            var sig = await _repo.GetSignature(userId, waiver.Id);
            var user = await _users.GetById(userId);
            return new ApiResponses().OkResult(new WaiverSignatureStatusResponse
            {
                CurrentVersion = waiver.Version,
                HasSignedCurrent = sig is not null,
                SignatureId = sig?.Id,
                SignedAt = sig?.SignedAt,
                SignatureDataUrl = sig?.SignatureDataUrl,
                RiderIsMinor = WaiverPolicy.IsMinor(user?.Birthdate),
                SignedByParent = sig?.SignedByParent ?? false,
                ParentName = sig?.ParentName,
                ParentPhone = sig?.ParentPhone,
                RiderHasEmergencyContact = !string.IsNullOrWhiteSpace(user?.EmergencyContactPhone),
            });
        }

        // Per-waiver Sign — same flow as POST /Waiver/Sign but for an explicit waiver id.
        [Authorize]
        [HttpPost("{id:guid}/Sign")]
        public async Task<IActionResult> SignWaiverById(Guid id, [FromBody] SignWaiverRequest? request)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!TryGetUserId(out var userId)) return new ApiResponses().BadRequestResult("Invalid token.");
            var waiver = await _repo.GetById(id, _tenantContext.TenantId);
            if (waiver is null) return new ApiResponses().NotFoundResult("Waiver not found.");
            if (waiver.ExpiresAt.HasValue && waiver.ExpiresAt.Value <= DateTime.UtcNow)
            {
                return new ApiResponses().BadRequestResult("This waiver has expired and can no longer be signed.");
            }
            return await SignWaiverInternal(userId, waiver, request);
        }

        [Authorize]
        [HttpPost("Sign")]
        public async Task<IActionResult> Sign([FromBody] SignWaiverRequest? request)
        {
            if (!_tenantContext.IsResolved)
            {
                return new ApiResponses().BadRequestResult("No tenant resolved.");
            }

            var active = await _repo.GetActive(_tenantContext.TenantId);
            if (active is null)
            {
                return new ApiResponses().NotFoundResult("No active waiver.");
            }

            if (!TryGetUserId(out var userId))
            {
                return new ApiResponses().BadRequestResult("Invalid token.");
            }

            return await SignWaiverInternal(userId, active, request);
        }

        // Shared sign body — validates signature + minor rules then writes the row.
        private async Task<IActionResult> SignWaiverInternal(Guid userId, TenantWaiver waiver, SignWaiverRequest? request)
        {
            var dataUrl = request?.SignatureDataUrl;
            if (!IsValidPngDataUrl(dataUrl))
            {
                return new ApiResponses().BadRequestResult("A handwritten signature is required.");
            }

            var user = await _users.GetById(userId);
            var isMinor = WaiverPolicy.IsMinor(user?.Birthdate);
            string? parentName = null;
            string? parentPhone = null;
            if (isMinor)
            {
                parentName = string.IsNullOrWhiteSpace(request?.ParentName) ? null : request!.ParentName!.Trim();
                parentPhone = string.IsNullOrWhiteSpace(request?.ParentPhone) ? null : request!.ParentPhone!.Trim();
                if (parentName is null || parentPhone is null || parentPhone.Length < 7)
                {
                    return new ApiResponses().BadRequestResult("A parent or guardian must sign for riders under 18 — please provide their name and phone number.");
                }
            }

            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            var sigId = await _repo.Sign(_tenantContext.TenantId, userId, waiver.Id, ip, dataUrl,
                signedByParent: isMinor, parentName: parentName, parentPhone: parentPhone);

            return new ApiResponses().OkResult(new WaiverSignatureStatusResponse
            {
                CurrentVersion = waiver.Version,
                HasSignedCurrent = true,
                SignatureId = sigId,
                SignedAt = DateTime.UtcNow,
                SignatureDataUrl = dataUrl,
                RiderIsMinor = isMinor,
                SignedByParent = isMinor,
                ParentName = parentName,
                ParentPhone = parentPhone,
            });
        }

        // ── Admin: Signed Waivers log / People view ─────────────────────────────
        // Signature data is customer PII, so these sit behind CustomersView (like the
        // Customers pages) rather than CatalogManage, which gates waiver *editing*.

        [Authorize(Policy = TenantPermissions.Policy.CustomersView)]
        [HttpGet("Signatures")]
        public async Task<IActionResult> ListSignatures(
            [FromQuery] string? search, [FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc,
            [FromQuery] Guid? waiverId, [FromQuery] bool minorsOnly = false,
            [FromQuery] string? context = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);
            var (rows, total) = await _repo.ListSignatures(_tenantContext.TenantId,
                search, fromUtc?.ToUniversalTime(), toUtc?.ToUniversalTime(), waiverId,
                minorsOnly, context, page, pageSize);
            return new ApiResponses().OkResult(new WaiverSignaturesPageResponse
            {
                Items = rows.Select(r => new WaiverSignatureItem
                {
                    Id = r.Id,
                    SignedAtUtc = DateTime.SpecifyKind(r.SignedAt, DateTimeKind.Utc),
                    UserId = r.UserId,
                    SignerName = r.SignerName,
                    SignerEmail = r.SignerEmail,
                    Birthdate = r.Birthdate,
                    SignedByParent = r.SignedByParent,
                    ParentName = r.ParentName,
                    ParentPhone = r.ParentPhone,
                    WaiverName = r.WaiverName,
                    WaiverVersion = r.WaiverVersion,
                    WaiverIsCurrent = r.WaiverIsCurrent,
                    Context = r.FromTicket ? "ticket" : r.FromRental ? "rental" : "account",
                }).ToList(),
                Total = total,
                Page = page,
                PageSize = pageSize,
            });
        }

        [Authorize(Policy = TenantPermissions.Policy.CustomersView)]
        [HttpGet("People")]
        public async Task<IActionResult> ListPeople(
            [FromQuery] string? search, [FromQuery] string? status, [FromQuery] bool agingOut = false,
            [FromQuery] bool minorsOnly = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);
            var (rows, total) = await _repo.ListPeople(_tenantContext.TenantId,
                search, status, agingOut, minorsOnly, page, pageSize);
            var now = DateTime.UtcNow;
            return new ApiResponses().OkResult(new WaiverPeoplePageResponse
            {
                Items = rows.Select(r =>
                {
                    var eighteenth = r.Birthdate?.AddYears(18);
                    return new WaiverPersonItem
                    {
                        PersonKey = r.PersonKey,
                        UserId = r.UserId,
                        PersonName = r.PersonName,
                        PersonEmail = r.PersonEmail,
                        Birthdate = r.Birthdate,
                        IsMinor = eighteenth.HasValue ? eighteenth.Value > now : r.HasGuardianSignature,
                        AgingOutSoon = eighteenth.HasValue && eighteenth.Value > now && eighteenth.Value <= now.AddDays(90),
                        HasGuardianSignature = r.HasGuardianSignature,
                        GuardianName = r.GuardianName,
                        GuardianPhone = r.GuardianPhone,
                        LastSignedAtUtc = DateTime.SpecifyKind(r.LastSignedAt, DateTimeKind.Utc),
                        SignatureCount = r.SignatureCount,
                        HasCurrentWaiver = r.HasCurrentWaiver,
                    };
                }).ToList(),
                Total = total,
                Page = page,
                PageSize = pageSize,
            });
        }

        [Authorize(Policy = TenantPermissions.Policy.CustomersView)]
        [HttpGet("Signatures/{id:guid}")]
        public async Task<IActionResult> GetSignatureDetail(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var row = await _repo.GetSignatureDetail(id, _tenantContext.TenantId);
            if (row is null) return new ApiResponses().NotFoundResult("Signature not found.");
            return new ApiResponses().OkResult(new WaiverSignatureDetailResponse
            {
                Id = row.Id,
                SignedAtUtc = DateTime.SpecifyKind(row.SignedAt, DateTimeKind.Utc),
                UserId = row.UserId,
                SignerName = row.SignerName,
                SignerEmail = row.SignerEmail,
                Birthdate = row.Birthdate,
                SignedByParent = row.SignedByParent,
                ParentName = row.ParentName,
                ParentPhone = row.ParentPhone,
                IpAddress = row.IpAddress,
                SignatureDataUrl = row.SignatureDataUrl,
                WaiverName = row.WaiverName,
                WaiverTitle = row.WaiverTitle,
                WaiverVersion = row.WaiverVersion,
                EmergencyContactName = row.EmergencyContactName,
                EmergencyContactPhone = row.EmergencyContactPhone,
                TicketEventTitle = row.TicketEventTitle,
                RentalLabel = row.RentalLabel,
            });
        }

        // ── Admin: Compliance Today ─────────────────────────────────────────────

        [Authorize(Policy = TenantPermissions.Policy.CustomersView)]
        [HttpGet("Compliance/Today")]
        public async Task<IActionResult> ComplianceToday()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var tenant = _tenantContext.Tenant;
            TimeZoneInfo tzInfo;
            try { tzInfo = TimeZoneInfo.FindSystemTimeZoneById(tenant.Timezone ?? "UTC"); }
            catch (TimeZoneNotFoundException) { tzInfo = TimeZoneInfo.Utc; }
            var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tzInfo);
            var dayStartUtc = TimeZoneInfo.ConvertTimeToUtc(localNow.Date, tzInfo);
            var dayEndUtc = dayStartUtc.AddDays(1);

            var rows = await _repo.ComplianceToday(_tenantContext.TenantId, dayStartUtc, dayEndUtc);
            var items = rows.Select(r => new WaiverComplianceItem
            {
                Source = r.Source,
                Label = r.Label,
                PersonName = r.PersonName,
                Email = r.Email,
                AtUtc = DateTime.SpecifyKind(r.At, DateTimeKind.Utc),
                WaiverStatus = (r.SignedForThis || r.HasCurrentWaiver) ? "signed" : "missing",
            }).ToList();
            return new ApiResponses().OkResult(new WaiverComplianceResponse
            {
                Items = items,
                TotalOnSite = items.Count,
                MissingCount = items.Count(i => i.WaiverStatus == "missing"),
            });
        }

        // ── Admin: signature requests (send a signing link) ─────────────────────

        [Authorize(Policy = TenantPermissions.Policy.CustomersView)]
        [HttpGet("SignRequests")]
        public async Task<IActionResult> ListSignRequests([FromQuery] string? search,
            [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);
            var (rows, total) = await _signRequests.List(_tenantContext.TenantId, search, status, page, pageSize);
            return new ApiResponses().OkResult(new WaiverSignRequestsPageResponse
            {
                Items = rows.Select(ToRequestItem).ToList(),
                Total = total,
                Page = page,
                PageSize = pageSize,
            });
        }

        [Authorize(Policy = TenantPermissions.Policy.CustomersView)]
        [HttpPost("SignRequests")]
        public async Task<IActionResult> CreateSignRequest([FromBody] CreateWaiverSignRequestRequest request)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var email = request.Email?.Trim();
            if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
                return new ApiResponses().BadRequestResult("A valid recipient email is required.");
            if (request.WaiverId.HasValue)
            {
                var waiver = await _repo.GetById(request.WaiverId.Value, _tenantContext.TenantId);
                if (waiver is null) return new ApiResponses().NotFoundResult("Waiver not found.");
            }
            else if (await _repo.GetActive(_tenantContext.TenantId) is null)
            {
                return new ApiResponses().BadRequestResult("This tenant has no active waiver to request a signature for.");
            }

            TryGetUserId(out var adminId);
            var row = await _signRequests.Create(_tenantContext.TenantId, request.WaiverId,
                NewToken(), email, string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim(),
                eventId: null, requestedByUserId: adminId == Guid.Empty ? null : adminId);

            var sent = await TrySendRequestEmail(row);
            if (sent) row = (await _signRequests.GetById(row.Id, _tenantContext.TenantId))!;
            return new ApiResponses().OkResult(ToRequestItem(row));
        }

        [Authorize(Policy = TenantPermissions.Policy.CustomersView)]
        [HttpPost("SignRequests/Bulk")]
        public async Task<IActionResult> CreateBulkSignRequests([FromBody] BulkWaiverSignRequestRequest request)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var ev = await _events.GetById(request.EventId, _tenantContext.TenantId);
            if (ev is null) return new ApiResponses().NotFoundResult("Event not found.");
            if (await _repo.GetActive(_tenantContext.TenantId) is null)
                return new ApiResponses().BadRequestResult("This tenant has no active waiver to request signatures for.");
            if (!_emailer.IsConfigured)
                return new ApiResponses().BadRequestResult("Email isn't set up on this server, so a roster send can't go out.");

            var rosterCount = await _signRequests.CountRosterEmails(request.EventId, _tenantContext.TenantId);
            var candidates = await _signRequests.CandidatesForEvent(request.EventId, _tenantContext.TenantId);
            TryGetUserId(out var adminId);
            int created = 0, emailFailures = 0;
            foreach (var c in candidates)
            {
                var row = await _signRequests.Create(_tenantContext.TenantId, waiverId: null,
                    NewToken(), c.Email, c.Name, eventId: request.EventId,
                    requestedByUserId: adminId == Guid.Empty ? null : adminId);
                created++;
                if (!await TrySendRequestEmail(row)) emailFailures++;
            }
            return new ApiResponses().OkResult(new BulkWaiverSignRequestResponse
            {
                Created = created,
                AlreadyCovered = Math.Max(0, rosterCount - created),
                EmailFailures = emailFailures,
            });
        }

        [Authorize(Policy = TenantPermissions.Policy.CustomersView)]
        [HttpPost("SignRequests/{id:guid}/Resend")]
        public async Task<IActionResult> ResendSignRequest(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var row = await _signRequests.GetById(id, _tenantContext.TenantId);
            if (row is null) return new ApiResponses().NotFoundResult("Request not found.");
            if (row.Status is "signed" or "cancelled")
                return new ApiResponses().BadRequestResult($"This request is already {row.Status}, so there's nothing to resend.");
            if (!await TrySendRequestEmail(row))
                return new ApiResponses().BadRequestResult("Could not send the email. Check the address and the server's email settings.");
            return new ApiResponses().OkResult(ToRequestItem((await _signRequests.GetById(id, _tenantContext.TenantId))!));
        }

        [Authorize(Policy = TenantPermissions.Policy.CustomersView)]
        [HttpPost("SignRequests/{id:guid}/Cancel")]
        public async Task<IActionResult> CancelSignRequest(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var row = await _signRequests.GetById(id, _tenantContext.TenantId);
            if (row is null) return new ApiResponses().NotFoundResult("Request not found.");
            if (row.Status == "signed")
                return new ApiResponses().BadRequestResult("This request was already signed and can't be cancelled.");
            await _signRequests.Cancel(id, _tenantContext.TenantId);
            return new ApiResponses().OkResult();
        }

        // ── Public: the emailed signing link (token is the credential) ──────────

        [HttpGet("SignRequest/{token}")]
        public async Task<IActionResult> GetSignRequestByToken(string token)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var row = await _signRequests.GetByToken(token);
            // Tenant mismatch reads as not-found so tokens can't be probed across subdomains.
            if (row is null || row.TenantId != _tenantContext.TenantId)
                return new ApiResponses().NotFoundResult("This signing link isn't valid.");
            if (row.Status == "cancelled")
                return new ApiResponses().NotFoundResult("This signing link is no longer active. Ask the venue to send a new one.");

            var waiver = row.WaiverId.HasValue
                ? await _repo.GetById(row.WaiverId.Value, _tenantContext.TenantId)
                : await _repo.GetActive(_tenantContext.TenantId);
            if (waiver is null)
                return new ApiResponses().NotFoundResult("The waiver behind this link is no longer available. Ask the venue to send a new one.");

            if (row.Status is "pending" or "sent")
                await _signRequests.MarkOpened(row.Id, _tenantContext.TenantId);

            return new ApiResponses().OkResult(new PublicWaiverSignRequestResponse
            {
                Status = row.Status == "signed" ? "signed" : "opened",
                RecipientName = row.RecipientName,
                RecipientEmail = row.RecipientEmail,
                WaiverId = waiver.Id,
                WaiverName = waiver.Name,
                WaiverTitle = waiver.Title,
                WaiverBody = waiver.Body,
                WaiverVersion = waiver.Version,
                AlreadySigned = row.Status == "signed",
            });
        }

        [HttpPost("SignRequest/{token}/Sign")]
        public async Task<IActionResult> SignByToken(string token, [FromBody] SignByTokenRequest request)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var row = await _signRequests.GetByToken(token);
            if (row is null || row.TenantId != _tenantContext.TenantId)
                return new ApiResponses().NotFoundResult("This signing link isn't valid.");
            if (row.Status == "cancelled")
                return new ApiResponses().BadRequestResult("This signing link was cancelled. Ask the venue to send a new one.");
            if (row.Status == "signed")
                return new ApiResponses().BadRequestResult("This waiver has already been signed through this link.");

            var first = request.FirstName?.Trim();
            var last = request.LastName?.Trim();
            if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(last))
                return new ApiResponses().BadRequestResult("First and last name are required.");
            if (!IsValidPngDataUrl(request.SignatureDataUrl))
                return new ApiResponses().BadRequestResult("A handwritten signature is required.");

            var waiver = row.WaiverId.HasValue
                ? await _repo.GetById(row.WaiverId.Value, _tenantContext.TenantId)
                : await _repo.GetActive(_tenantContext.TenantId);
            if (waiver is null)
                return new ApiResponses().BadRequestResult("The waiver behind this link is no longer available. Ask the venue to send a new one.");
            if (waiver.ExpiresAt.HasValue && waiver.ExpiresAt.Value <= DateTime.UtcNow)
                return new ApiResponses().BadRequestResult("This waiver has expired and can no longer be signed.");

            var isMinor = WaiverPolicy.IsMinor(request.Birthdate);
            string? parentName = null, parentPhone = null;
            if (isMinor)
            {
                parentName = string.IsNullOrWhiteSpace(request.ParentName) ? null : request.ParentName!.Trim();
                parentPhone = string.IsNullOrWhiteSpace(request.ParentPhone) ? null : request.ParentPhone!.Trim();
                if (parentName is null || parentPhone is null || parentPhone.Length < 7)
                    return new ApiResponses().BadRequestResult("A parent or guardian must sign for riders under 18 — please provide their name and phone number.");
            }

            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            var sigId = await _repo.SignRegistrant(_tenantContext.TenantId, waiver.Id, ip,
                request.SignatureDataUrl, signerEmail: row.RecipientEmail,
                signerName: $"{first} {last}",
                attendeeFirstName: first!, attendeeLastName: last!,
                attendeeBirthdate: request.Birthdate,
                signedByParent: isMinor, parentName: parentName, parentPhone: parentPhone);
            await _signRequests.MarkSigned(row.Id, _tenantContext.TenantId, sigId);
            return new ApiResponses().OkResult();
        }

        // ── Signature-request helpers ───────────────────────────────────────────

        private static string NewToken() =>
            Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

        private string BuildSigningLink(string token)
        {
            var apex = _config["App:RootDomain"] ?? "ridepass.io";
            return $"https://{_tenantContext.Tenant.Subdomain}.{apex}/SignWaiver/{token}";
        }

        private WaiverSignRequestItem ToRequestItem(Services.Repositories.Data.WaiverData.WaiverSignRequestRow r) => new()
        {
            Id = r.Id,
            RecipientEmail = r.RecipientEmail,
            RecipientName = r.RecipientName,
            WaiverName = r.WaiverName,
            WaiverVersion = r.WaiverVersion,
            EventTitle = r.EventTitle,
            Status = r.Status,
            CreatedAtUtc = DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc),
            SentAtUtc = r.SentAt.HasValue ? DateTime.SpecifyKind(r.SentAt.Value, DateTimeKind.Utc) : null,
            OpenedAtUtc = r.OpenedAt.HasValue ? DateTime.SpecifyKind(r.OpenedAt.Value, DateTimeKind.Utc) : null,
            SignedAtUtc = r.SignedAt.HasValue ? DateTime.SpecifyKind(r.SignedAt.Value, DateTimeKind.Utc) : null,
            Link = BuildSigningLink(r.Token),
        };

        /// <summary>Sends the signing email and marks the request sent. Returns false when the
        /// emailer is unconfigured or the send fails; the request stays pending so the admin
        /// can copy the link manually or retry.</summary>
        private async Task<bool> TrySendRequestEmail(Services.Repositories.Data.WaiverData.WaiverSignRequestRow row)
        {
            if (!_emailer.IsConfigured) return false;
            var tenant = _tenantContext.Tenant;
            var link = BuildSigningLink(row.Token);
            static string Enc(string s) => System.Net.WebUtility.HtmlEncode(s);
            var html =
                $"<div style=\"font-family:Arial,Helvetica,sans-serif;max-width:480px\">" +
                $"<h2 style=\"margin:0 0 8px\">{Enc(tenant.DisplayName)}</h2>" +
                $"<p>Hi {Enc(row.RecipientName ?? "there")},</p>" +
                $"<p>Please sign our waiver before your visit. It only takes a minute, and it means " +
                $"no paperwork holds you up when you arrive:</p>" +
                $"<p style=\"margin:16px 0\"><a href=\"{link}\" style=\"background:#1976d2;color:#fff;padding:10px 18px;" +
                $"border-radius:6px;text-decoration:none\">Sign the waiver</a></p>" +
                $"<p style=\"font-size:12px;color:#666\">Or paste this link into your browser:<br/>{link}</p></div>";
            var ok = await _emailer.Send(row.RecipientEmail,
                $"{tenant.DisplayName}: please sign the waiver before your visit",
                html, null, Services.Email.TenantEmailIdentity.For(tenant));
            if (ok) await _signRequests.MarkSent(row.Id, _tenantContext.TenantId);
            return ok;
        }

        private static bool IsValidPngDataUrl(string? dataUrl)
        {
            if (string.IsNullOrWhiteSpace(dataUrl)) return false;
            if (!dataUrl.StartsWith("data:image/png;base64,", StringComparison.Ordinal)) return false;
            // 800 chars ≈ a non-empty signature; cap at 1 MB to avoid abuse.
            return dataUrl.Length is > 800 and < 1_400_000;
        }

        private bool TryGetUserId(out Guid userId)
        {
            var claim = User.FindFirst("UserId")?.Value;
            return Guid.TryParse(claim, out userId);
        }
    }
}
