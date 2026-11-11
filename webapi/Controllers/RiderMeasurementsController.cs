using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Interfaces;
using Services.Riders;
using webapi.Controllers.API.Data.RiderMeasurement;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    /// <summary>
    /// A rider's own height and weight (Script0317), and their dependents' at this venue, for the profile page
    /// and to prefill lesson checkout. Self-service only: the signed-in account and the minors it is guardian of.
    /// </summary>
    [ApiController]
    [Route("api/RiderMeasurements")]
    [Authorize]
    public class RiderMeasurementsController : ControllerBase
    {
        private readonly IRiderMeasurementRepository _measurements;
        private readonly ITenantContext _tenantContext;

        public RiderMeasurementsController(IRiderMeasurementRepository measurements, ITenantContext tenantContext)
        {
            _measurements = measurements;
            _tenantContext = tenantContext;
        }

        private bool TryGetSelfId(out Guid id) => Guid.TryParse(User.FindFirst("UserId")?.Value, out id);

        [HttpGet("Mine")]
        public async Task<IActionResult> Mine()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!TryGetSelfId(out var userId)) return new ApiResponses().BadRequestResult("Invalid token.");
            var tenantId = _tenantContext.TenantId;
            var self = await _measurements.GetForUser(userId, tenantId);
            if (self is null) return new ApiResponses().NotFoundResult("Account not found.");
            var dependents = await _measurements.ListDependents(tenantId, userId);
            return new ApiResponses().OkResult(new MyMeasurementsResponse
            {
                Self = new RiderMeasurementDto { HeightIn = self.HeightIn, WeightLb = self.WeightLb, MeasuredAt = Utc(self.MeasuredAt) },
                Dependents = dependents.Select(d => new DependentMeasurementDto
                {
                    Id = d.Id,
                    FirstName = d.FirstName,
                    LastName = d.LastName,
                    HeightIn = d.HeightIn,
                    WeightLb = d.WeightLb,
                    MeasuredAt = Utc(d.MeasuredAt),
                }).ToList(),
            });
        }

        [HttpPut("Mine")]
        public async Task<IActionResult> UpdateMine([FromBody] UpdateMeasurementRequest request)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!TryGetSelfId(out var userId)) return new ApiResponses().BadRequestResult("Invalid token.");
            if (RiderMeasurementRules.Validate(request.HeightIn, request.WeightLb, required: true) is { } error)
                return new ApiResponses().BadRequestResult(error);
            if (!await _measurements.SaveForUser(userId, _tenantContext.TenantId, request.HeightIn!.Value, request.WeightLb!.Value))
                return new ApiResponses().NotFoundResult("Account not found.");
            return new ApiResponses().OkResult(new RiderMeasurementDto
            {
                HeightIn = request.HeightIn, WeightLb = request.WeightLb, MeasuredAt = DateTime.UtcNow,
            });
        }

        /// <summary>A dependent's numbers: only the signed-in guardian's own dependent at this venue.</summary>
        [HttpPut("Dependent/{id:guid}")]
        public async Task<IActionResult> UpdateDependent(Guid id, [FromBody] UpdateMeasurementRequest request)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!TryGetSelfId(out var userId)) return new ApiResponses().BadRequestResult("Invalid token.");
            if (RiderMeasurementRules.Validate(request.HeightIn, request.WeightLb, required: true) is { } error)
                return new ApiResponses().BadRequestResult(error);
            if (!await _measurements.SaveForDependent(id, _tenantContext.TenantId, userId, request.HeightIn!.Value, request.WeightLb!.Value))
                return new ApiResponses().NotFoundResult("Rider not found.");
            return new ApiResponses().OkResult(new RiderMeasurementDto
            {
                HeightIn = request.HeightIn, WeightLb = request.WeightLb, MeasuredAt = DateTime.UtcNow,
            });
        }

        private static DateTime? Utc(DateTime? d) => d is { } v ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : null;
    }
}
