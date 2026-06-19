using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using webapi.AuthPolicies;
using webapi.Sync;

namespace webapi.Controllers
{
    /// <summary>
    /// The DESTINATION side of stage->prod tenant promotion, on prod. Super-admin only. It
    /// pulls a tenant's bundle from staging (via TenantSyncClient) and imports it, with a
    /// preview step so the operator can see create / replace / blocked before committing.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = SuperAdminRequirement.PolicyName)]
    public class TenantPromotionController : ControllerBase
    {
        private readonly TenantPromotionService _promotion;
        private readonly TenantSyncClient _client;

        public TenantPromotionController(TenantPromotionService promotion, TenantSyncClient client)
        {
            _promotion = promotion;
            _client = client;
        }

        /// <summary>Unpublished tenants available to promote (proxied from staging).</summary>
        [HttpGet("StageTenants")]
        public async Task<IActionResult> StageTenants(CancellationToken ct)
        {
            if (!_client.IsConfigured)
            {
                return new ApiResponses().BadRequestResult("Stage sync isn't configured (TenantSync:SourceBaseUrl / Key).");
            }
            try
            {
                var json = await _promotion.ListStageTenantsJson(ct);
                return Content(json, "application/json");
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult($"Couldn't reach staging: {ex.Message}");
            }
        }

        /// <summary>
        /// confirm=false returns a preview (create / replace / blocked + counts) without
        /// writing; confirm=true performs the import unless blocked.
        /// </summary>
        [HttpPost("Promote/{stageTenantId:guid}")]
        public async Task<IActionResult> Promote(Guid stageTenantId, [FromQuery] bool confirm, CancellationToken ct)
        {
            if (!_client.IsConfigured)
            {
                return new ApiResponses().BadRequestResult("Stage sync isn't configured.");
            }
            try
            {
                var result = await _promotion.Promote(stageTenantId, confirm, ct);
                return new ApiResponses().OkResult(result);
            }
            catch (Exception ex)
            {
                return new ApiResponses().BadRequestResult($"Promotion failed: {ex.Message}");
            }
        }
    }
}
