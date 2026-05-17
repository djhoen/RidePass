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

        public WaiverController(
            IWaiverRepository repo,
            IEventRepository events,
            IUserRepository users,
            ITenantContext tenantContext)
        {
            _repo = repo;
            _events = events;
            _users = users;
            _tenantContext = tenantContext;
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
