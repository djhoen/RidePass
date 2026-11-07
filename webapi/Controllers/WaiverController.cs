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
        private readonly Services.Waivers.IWaiverCheckInGate _waiverGate;
        private readonly Services.Audit.IAuditLogger _audit;

        public WaiverController(
            IWaiverRepository repo,
            IEventRepository events,
            IUserRepository users,
            ITenantContext tenantContext,
            IWaiverSignRequestRepository signRequests,
            Services.Helpers.ISmtpEmailer emailer,
            IConfiguration config,
            Services.Waivers.IWaiverCheckInGate waiverGate,
            Services.Audit.IAuditLogger audit)
        {
            _waiverGate = waiverGate;
            _audit = audit;
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
                // A guardian / pre-18 signature no longer counts once the rider is 18: ask them to sign.
                HasSignedCurrent = sig is not null
                    && !AccountWaiverStatus.IsMinorEraForAdult(user, sig, _tenantContext.Tenant.Timezone, DateTime.UtcNow),
                SignatureId = sig?.Id,
                SignedAt = sig?.SignedAt,
                SignatureDataUrl = sig?.SignatureDataUrl,
                RiderIsMinor = AccountWaiverStatus.IsMinorToday(user, _tenantContext.Tenant.Timezone, DateTime.UtcNow),
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
                HasSignedCurrent = sig is not null
                    && !AccountWaiverStatus.IsMinorEraForAdult(user, sig, _tenantContext.Tenant.Timezone, DateTime.UtcNow),
                SignatureId = sig?.Id,
                SignedAt = sig?.SignedAt,
                SignatureDataUrl = sig?.SignatureDataUrl,
                RiderIsMinor = AccountWaiverStatus.IsMinorToday(user, _tenantContext.Tenant.Timezone, DateTime.UtcNow),
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
            // Minor by the gate's own birthdate source (ID-verified, else self-reported) and the
            // tenant's local date: the shared helper, so the app can't disagree with the gate.
            var isMinor = AccountWaiverStatus.IsMinorToday(user, _tenantContext.Tenant.Timezone, DateTime.UtcNow);
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

            // Their current signature is from their minor years (a guardian's, or made before their
            // 18th birthday) and they are an adult now: this is a new signature, not a refresh.
            var supersede = !isMinor && AccountWaiverStatus.IsMinorEraForAdult(user,
                await _repo.GetSignature(userId, waiver.Id), _tenantContext.Tenant.Timezone, DateTime.UtcNow);
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            // An adult signing for themselves records who it is for (their name and birthdate), so the
            // gate's adult-signature lookup recognises it on their tickets and passes.
            var sigId = await _repo.Sign(_tenantContext.TenantId, userId, waiver.Id, ip, dataUrl,
                signedByParent: isMinor, parentName: parentName, parentPhone: parentPhone, supersede: supersede,
                attendeeFirstName: isMinor ? null : user?.FirstName, attendeeLastName: isMinor ? null : user?.LastName,
                attendeeBirthdate: isMinor ? null : AccountWaiverStatus.Birthdate(user)?.ToDateTime(TimeOnly.MinValue));
            // Re-signed as an adult over a minor-era signature: their own paid credentials still pointing
            // at the old one are relinked (gate linking rules).
            if (supersede)
                await _waiverGate.RelinkAfterAdultResign(_tenantContext.TenantId, userId, sigId, waiver.Id);

            // Report what is STORED: an adult re-signing over their guardian's signature gets a new
            // row (the old one is kept detached), a same-kind re-sign keeps the original row.
            var stored = await _repo.GetSignature(userId, waiver.Id);
            return new ApiResponses().OkResult(new WaiverSignatureStatusResponse
            {
                CurrentVersion = waiver.Version,
                HasSignedCurrent = true,
                SignatureId = stored?.Id ?? sigId,
                SignedAt = stored?.SignedAt ?? DateTime.UtcNow,
                SignatureDataUrl = stored?.SignatureDataUrl ?? dataUrl,
                RiderIsMinor = isMinor,
                SignedByParent = stored?.SignedByParent ?? isMinor,
                ParentName = stored is not null ? stored.ParentName : parentName,
                ParentPhone = stored is not null ? stored.ParentPhone : parentPhone,
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
            [FromQuery] string? context = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
            [FromQuery] string? personKey = null)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);
            var (rows, total) = await _repo.ListSignatures(_tenantContext.TenantId,
                search, fromUtc?.ToUniversalTime(), toUtc?.ToUniversalTime(), waiverId,
                minorsOnly, context, page, pageSize, personKey);
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
                search, status, agingOut, minorsOnly, page, pageSize,
                TenantLocalToday(), TenantClock.IanaId(_tenantContext.Tenant.Timezone));
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
                        NeedsAdultWaiver = r.NeedsAdultWaiver,
                        AdultLinkOpen = r.AdultLinkOpen,
                        HasRiderEmail = r.RiderAccountId is not null && !string.IsNullOrWhiteSpace(r.AccountEmail),
                    };
                }).ToList(),
                Total = total,
                Page = page,
                PageSize = pageSize,
            });
        }

        private DateTime TenantLocalToday() =>
            TenantClock.LocalDate(_tenantContext.Tenant.Timezone, DateTime.UtcNow).ToDateTime(TimeOnly.MinValue);

        /// <summary>
        /// Ahead of the gate: send an adult-waiver signing link to people who have turned 18 since
        /// their waiver ("Needs adult waiver" in Signed Waivers), so they sign as adults before their
        /// next visit. Staff-triggered only, for exactly the people on screen (their person keys); each
        /// is re-checked against the same rule before anything is sent.
        ///
        /// - Only to the RIDER's own account email, never a signer's (often a parent's) address;
        ///   anyone without one is skipped as "no rider email".
        /// - Targeted at what the gate will check: their next ticket or pass when it is failing on the
        ///   adult rule (the gate's own target resolution, so the signature is linked there), else
        ///   the active waiver with the rider's identity on the request (Script0306), so the
        ///   signature carries their name and birthdate and is attached to their account.
        /// - One open link per RIDER (not per email): a rider with an open link is skipped.
        /// - At most MaxAdultLinksPerSend per request (the rest are reported, not sent); emails go out
        ///   with bounded concurrency. Without an email server the links are still created and can be
        ///   copied from Waiver requests.
        /// </summary>
        private const int MaxAdultLinksPerSend = 50;

        [Authorize(Policy = TenantPermissions.Policy.CustomersView)]
        [HttpPost("SignRequests/NeedsAdult")]
        public async Task<IActionResult> CreateAdultWaiverSignRequests([FromBody] BulkAdultWaiverSignRequestRequest? request)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var keys = request?.PersonKeys?.Where(k => !string.IsNullOrWhiteSpace(k)).ToHashSet() ?? new HashSet<string>();
            if (keys.Count == 0) return new ApiResponses().BadRequestResult("Pick the people to send a link to.");
            if (await _repo.GetActive(_tenantContext.TenantId) is null)
                return new ApiResponses().BadRequestResult("This tenant has no active waiver to request signatures for.");
            var tenantId = _tenantContext.TenantId;

            // Re-check: still "needs adult waiver" by the same rule as the list and the gate, for exactly
            // the requested people, filtered in SQL (one query).
            var (people, _) = await _repo.ListPeople(tenantId, null, "needs_adult",
                agingOut: false, minorsOnly: false, 1, keys.Count, TenantLocalToday(),
                TenantClock.IanaId(_tenantContext.Tenant.Timezone), keys);
            var resp = new AdultWaiverBulkResponse { NoLongerNeeded = keys.Count - people.Count };

            var withEmail = new List<(Services.Repositories.Data.WaiverData.WaiverPersonRow Person, Guid UserId, string Email)>();
            foreach (var p in people)
            {
                if (p.RiderAccountId is Guid uid && !string.IsNullOrWhiteSpace(p.AccountEmail) && p.AccountEmail.Contains('@'))
                    withEmail.Add((p, uid, p.AccountEmail.Trim()));
                else resp.SkippedNoRiderEmail++;
            }
            var open = await _signRequests.RidersWithOpenRequests(tenantId, withEmail.Select(x => x.UserId).ToList());
            var toCreate = withEmail.Where(x => !open.Contains(x.UserId)).ToList();
            resp.SkippedOpenLink = withEmail.Count - toCreate.Count;
            resp.Deferred = Math.Max(0, toCreate.Count - MaxAdultLinksPerSend);

            TryGetUserId(out var adminId);
            var created = new List<Services.Repositories.Data.WaiverData.WaiverSignRequestRow>();
            foreach (var (person, userId, email) in toCreate.Take(MaxAdultLinksPerSend))
            {
                // Re-check in C# that the account really is this person (the list resolves it in SQL):
                // the link must never go to anyone else's address.
                var user = await _users.GetById(userId);
                if (user is null || person.Birthdate is null
                    || (user.TenantId is Guid ut && ut != tenantId)
                    || Services.Riders.IdCheckRules.Normalize(person.PersonName)
                        != Services.Riders.IdCheckRules.Normalize($"{user.FirstName} {user.LastName}"))
                { resp.SkippedNoRiderEmail++; continue; }
                var rider = new Services.Repositories.Data.WaiverData.SignRequestRider(userId,
                    user.FirstName, user.LastName, person.Birthdate.Value.Date);

                // Their next credential the gate fails on the adult rule, if any: target it.
                Services.Waivers.WaiverSignTarget? target = null;
                foreach (var c in await _signRequests.UpcomingCredentialsForAccount(tenantId, userId))
                {
                    if (!Services.Riders.IdCheckRules.AccountIsRider(c.FirstName, c.LastName, user.FirstName, user.LastName)) continue;
                    var t = await _waiverGate.ResolveSignTarget(tenantId,
                        c.Kind == "ticket" ? c.Id : null, c.Kind == "season_pass" ? c.Id : null, null);
                    if (t?.Decision is { AdultWaiverNeeded: true, FixTarget: not null, RequiredWaiverId: not null })
                    { target = t; break; }
                }

                created.Add(await _signRequests.Create(tenantId, target?.Decision.RequiredWaiverId, NewToken(), email,
                    person.PersonName, target?.EventId, adminId == Guid.Empty ? null : adminId,
                    target?.TicketId, target?.PassPurchaseId, rider));
                if (target is not null) resp.TargetedCredential++;
            }
            resp.Created = created.Count;

            if (!_emailer.IsConfigured)
            {
                resp.EmailNotConfigured = true;
            }
            else
            {
                // Bounded concurrency: a few sends at a time, never the whole list at once.
                using var gate = new SemaphoreSlim(4);
                var results = await Task.WhenAll(created.Select(async row =>
                {
                    await gate.WaitAsync();
                    try { return await TrySendRequestEmail(row); }
                    finally { gate.Release(); }
                }));
                resp.Sent = results.Count(ok => ok);
                resp.EmailFailures = results.Count(ok => !ok);
            }
            return new ApiResponses().OkResult(resp);
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
                SignedAtUtc = r.SignedAt is null ? null : DateTime.SpecifyKind(r.SignedAt.Value, DateTimeKind.Utc),
                CheckedInAtUtc = r.CheckedInAt is null ? null : DateTime.SpecifyKind(r.CheckedInAt.Value, DateTimeKind.Utc),
                WaiverStatus = (r.SignedForThis || r.SignedAt is not null) ? "signed" : "missing",
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

            // A link the gate sent FOR a specific ticket or pass (Script0304): the gate's own view of
            // that rider decides who the waiver is for, how old they are, and where it is linked.
            // The form cannot talk its way out of the guardian path.
            Services.Waivers.WaiverSignTarget? target = null;
            if (row.EventTicketPurchaseId is not null || row.SeasonPassPurchaseId is not null)
            {
                target = await _waiverGate.ResolveSignTarget(_tenantContext.TenantId,
                    row.EventTicketPurchaseId, row.SeasonPassPurchaseId, row.EventId);
                if (target is null)
                    return new ApiResponses().BadRequestResult(
                        "The ticket or pass this link was sent for no longer exists. Ask the venue to send a new one.");
            }

            // An adult-waiver link sent ahead of time with no credential to target (Script0306) carries
            // the rider it is for: the signature is made AS them, whatever the form says.
            var riderIdentity = target is null && row.RiderUserId is Guid riderUserId
                && !string.IsNullOrWhiteSpace(row.RiderFirstName) && !string.IsNullOrWhiteSpace(row.RiderLastName)
                && row.RiderBirthdate is not null
                ? new Services.Repositories.Data.WaiverData.SignRequestRider(riderUserId,
                    row.RiderFirstName!.Trim(), row.RiderLastName!.Trim(), row.RiderBirthdate.Value)
                : null;

            // The attendee is the target rider (their name and the birthdate the gate uses), not
            // whatever was typed; a differing typed name is kept only as an audit note.
            var attendeeFirst = riderIdentity?.FirstName
                ?? (string.IsNullOrWhiteSpace(target?.FirstName) ? first! : target!.FirstName!.Trim());
            var attendeeLast = riderIdentity?.LastName
                ?? (string.IsNullOrWhiteSpace(target?.LastName) ? last! : target!.LastName!.Trim());
            var nameMismatch = (target is not null || riderIdentity is not null)
                && !(string.Equals(first, attendeeFirst, StringComparison.OrdinalIgnoreCase)
                     && string.Equals(last, attendeeLast, StringComparison.OrdinalIgnoreCase));
            var targetDob = target?.Decision.Birthdate;
            DateTime? birthdate = riderIdentity?.Birthdate
                ?? (targetDob is { } d ? d.ToDateTime(TimeOnly.MinValue) : request.Birthdate);
            var isMinor = target is { Decision: { RiderAudience: true, IsMinor: bool targetMinor } }
                ? targetMinor
                : WaiverPolicy.IsMinor(birthdate);

            // A link sent because the rider turned 18 since their waiver (from the gate, or ahead of
            // time from Signed Waivers): only THEIR adult signature fixes that, so the parent /
            // guardian path is refused rather than silently ignored.
            var adultOnly = target is { Decision.AdultWaiverNeeded: true } || (riderIdentity is not null && !isMinor);
            if (adultOnly && (!string.IsNullOrWhiteSpace(request.ParentName) || !string.IsNullOrWhiteSpace(request.ParentPhone)))
                return new ApiResponses().BadRequestResult(
                    $"{target?.DisplayName ?? $"{attendeeFirst} {attendeeLast}"} is 18 or over, so they must sign this waiver themselves, not a parent or guardian.");

            string? parentName = null, parentPhone = null;
            if (isMinor)
            {
                parentName = string.IsNullOrWhiteSpace(request.ParentName) ? null : request.ParentName!.Trim();
                parentPhone = string.IsNullOrWhiteSpace(request.ParentPhone) ? null : request.ParentPhone!.Trim();
                if (parentName is null || parentPhone is null || parentPhone.Length < 7)
                    return new ApiResponses().BadRequestResult(target is not null
                        ? $"{target.DisplayName} is under 18, so a parent or guardian has to sign this waiver. Enter the parent or guardian's name and phone number, then sign."
                        : "A parent or guardian must sign for riders under 18 — please provide their name and phone number.");
            }

            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            var sigId = await _repo.SignRegistrant(_tenantContext.TenantId, waiver.Id, ip,
                request.SignatureDataUrl, signerEmail: row.RecipientEmail,
                signerName: isMinor ? parentName : $"{attendeeFirst} {attendeeLast}",
                attendeeFirstName: attendeeFirst, attendeeLastName: attendeeLast,
                attendeeBirthdate: birthdate,
                signedByParent: isMinor, parentName: parentName, parentPhone: parentPhone);

            if (riderIdentity is not null && !isMinor)
            {
                // No credential to link: the signature becomes the rider's ACCOUNT signature for this
                // document (their minor-era one is kept as a record, detached), which is what the
                // gate reads for their passes; tickets find it by name and birthdate.
                var attached = await _repo.AttachAccountSignature(_tenantContext.TenantId, riderIdentity.UserId,
                    waiver.Id, sigId, replaceMinorEra: true);
                await _audit.Log("waiver.adult_sign_request.signed",
                    $"Adult waiver link signed by {attendeeFirst} {attendeeLast}"
                        + (nameMismatch ? $" (form named \"{first} {last}\")" : ""),
                    targetKind: "user", targetId: riderIdentity.UserId, tenantId: _tenantContext.TenantId,
                    metadata: new { requestId = row.Id, signatureId = sigId, attached, nameMismatch });
            }

            if (target is not null)
            {
                // Put the signature where the gate reads it, by the request's target ids; the gate
                // fills only what is missing or failing and never replaces a valid guardian signature.
                var linked = await _waiverGate.LinkSignedWaiver(_tenantContext.TenantId, target, sigId, waiver.Id);
                await _audit.Log("waiver.gate_sign_request.signed",
                    $"Gate waiver link signed for {target.DisplayName}"
                        + (isMinor ? $" by guardian {parentName}" : "")
                        + (nameMismatch ? $" (form named \"{first} {last}\")" : ""),
                    targetKind: target.TicketId is not null ? "event_ticket_purchase" : "season_pass_purchase",
                    targetId: target.TicketId ?? target.PassPurchaseId,
                    tenantId: _tenantContext.TenantId,
                    metadata: new { requestId = row.Id, signatureId = sigId, linked, nameMismatch, typedName = $"{first} {last}" });

                // Did it take? If the gate still fails for THIS document with a fix the link was for,
                // the signature could not be put where the gate reads it (e.g. the credential changed):
                // say so and keep the link usable rather than marking it signed with nothing fixed. The
                // signature itself is kept as the record.
                var after = await _waiverGate.ResolveSignTarget(_tenantContext.TenantId,
                    row.EventTicketPurchaseId, row.SeasonPassPurchaseId, row.EventId);
                if (linked == 0 && after?.Decision is { BlockReason: not null, FixTarget: not null } still
                    && still.RequiredWaiverId == waiver.Id)
                {
                    await _audit.Log("waiver.gate_sign_request.link_failed",
                        $"Gate waiver link for {target.DisplayName} was signed but could not be applied",
                        targetKind: target.TicketId is not null ? "event_ticket_purchase" : "season_pass_purchase",
                        targetId: target.TicketId ?? target.PassPurchaseId,
                        tenantId: _tenantContext.TenantId,
                        metadata: new { requestId = row.Id, signatureId = sigId, blockReason = still.BlockReason });
                    return new ApiResponses().BadRequestResult(
                        $"Your signature was saved, but it couldn't be applied to {target.DisplayName}'s "
                        + $"{(target.TicketId is not null ? "ticket" : "pass")}: {still.BlockReason} "
                        + "Please show this to the staff at the gate; this link stays open so it can be signed again.");
                }
            }
            await _signRequests.MarkSigned(row.Id, _tenantContext.TenantId, sigId);
            return new ApiResponses().OkResult();
        }

        // ── Signature-request helpers ───────────────────────────────────────────

        private static string NewToken() => WaiverSignRequestMail.NewToken();

        private string BuildSigningLink(string token) =>
            WaiverSignRequestMail.BuildSigningLink(_config, _tenantContext.Tenant, token);

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
        private Task<bool> TrySendRequestEmail(Services.Repositories.Data.WaiverData.WaiverSignRequestRow row) =>
            WaiverSignRequestMail.TrySend(_emailer, _config, _tenantContext.Tenant, _signRequests, row);

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
