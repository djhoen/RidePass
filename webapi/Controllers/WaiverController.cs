using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Waiver;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WaiverController : ControllerBase
    {
        private readonly IWaiverRepository _repo;
        private readonly ITenantContext _tenantContext;

        public WaiverController(IWaiverRepository repo, ITenantContext tenantContext)
        {
            _repo = repo;
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

            return new ApiResponses().OkResult(new WaiverResponse
            {
                Id = waiver.Id,
                Version = waiver.Version,
                Title = waiver.Title,
                Body = waiver.Body,
            });
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPut]
        public async Task<IActionResult> PublishNew([FromBody] UpdateWaiverRequest request)
        {
            var newWaiver = await _repo.PublishNewVersion(_tenantContext.TenantId, request.Title, request.Body);
            return new ApiResponses().OkResult(new WaiverResponse
            {
                Id = newWaiver.Id,
                Version = newWaiver.Version,
                Title = newWaiver.Title,
                Body = newWaiver.Body,
            });
        }

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
            return new ApiResponses().OkResult(new WaiverSignatureStatusResponse
            {
                CurrentVersion = active.Version,
                HasSignedCurrent = sig is not null,
                SignatureId = sig?.Id,
                SignedAt = sig?.SignedAt,
            });
        }

        [Authorize]
        [HttpPost("Sign")]
        public async Task<IActionResult> Sign()
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

            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            var sigId = await _repo.Sign(_tenantContext.TenantId, userId, active.Id, ip);

            return new ApiResponses().OkResult(new WaiverSignatureStatusResponse
            {
                CurrentVersion = active.Version,
                HasSignedCurrent = true,
                SignatureId = sigId,
                SignedAt = DateTime.UtcNow,
            });
        }

        private bool TryGetUserId(out Guid userId)
        {
            var claim = User.FindFirst("UserId")?.Value;
            return Guid.TryParse(claim, out userId);
        }
    }
}
