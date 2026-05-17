using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Data.RewardData;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Reward;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RewardController : ControllerBase
    {
        private readonly IRewardRepository _rewards;
        private readonly ITenantContext _tenantContext;

        public RewardController(IRewardRepository rewards, ITenantContext tenantContext)
        {
            _rewards = rewards;
            _tenantContext = tenantContext;
        }

        // ── Tenant admin ────────────────────────────────────────────────────────

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpGet("Programs/Admin")]
        public async Task<IActionResult> ListProgramsAdmin()
        {
            var rows = await _rewards.ListProgramsForTenant(_tenantContext.TenantId, activeOnly: false);
            return new ApiResponses().OkResult(rows.Select(ToResponse));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost("Programs")]
        public async Task<IActionResult> CreateProgram([FromBody] UpsertRewardProgramRequest request)
        {
            var program = new RewardProgram
            {
                TenantId = _tenantContext.TenantId,
                Name = request.Name.Trim(),
                Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
                EnrollmentMode = request.EnrollmentMode,
                RequirementKind = request.RequirementKind,
                RequirementCount = request.RequirementCount,
                RewardPercentOff = request.RewardPercentOff,
                ProximityEmailThreshold = request.ProximityEmailThreshold,
                IsActive = request.IsActive,
            };
            program.Id = await _rewards.CreateProgram(program);
            return new ApiResponses().OkResult(ToResponse(program));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPut("Programs/{id:guid}")]
        public async Task<IActionResult> UpdateProgram(Guid id, [FromBody] UpsertRewardProgramRequest request)
        {
            var existing = await _rewards.GetProgram(id, _tenantContext.TenantId);
            if (existing is null)
            {
                return new ApiResponses().NotFoundResult("Program not found.");
            }
            existing.Name = request.Name.Trim();
            existing.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
            existing.EnrollmentMode = request.EnrollmentMode;
            existing.RequirementKind = request.RequirementKind;
            existing.RequirementCount = request.RequirementCount;
            existing.RewardPercentOff = request.RewardPercentOff;
            existing.ProximityEmailThreshold = request.ProximityEmailThreshold;
            existing.IsActive = request.IsActive;
            await _rewards.UpdateProgram(existing);
            return new ApiResponses().OkResult(ToResponse(existing));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpDelete("Programs/{id:guid}")]
        public async Task<IActionResult> DeleteProgram(Guid id)
        {
            var existing = await _rewards.GetProgram(id, _tenantContext.TenantId);
            if (existing is null)
            {
                return new ApiResponses().NotFoundResult("Program not found.");
            }
            await _rewards.DeleteProgram(id, _tenantContext.TenantId);
            return new ApiResponses().OkResult();
        }

        // ── Rider ────────────────────────────────────────────────────────────────

        [Authorize]
        [HttpGet("Mine")]
        public async Task<IActionResult> ListMyPrograms()
        {
            if (!_tenantContext.IsResolved)
            {
                return new ApiResponses().BadRequestResult("No tenant resolved.");
            }
            if (!TryGetUserId(out var userId))
            {
                return new ApiResponses().BadRequestResult("Invalid token.");
            }

            var programs = await _rewards.ListProgramsForTenant(_tenantContext.TenantId, activeOnly: true);
            var enrollments = (await _rewards.ListEnrollmentsForUser(userId))
                .ToDictionary(e => e.ProgramId);

            var responses = new List<RiderRewardProgramResponse>();
            foreach (var p in programs)
            {
                var enrolled = enrollments.TryGetValue(p.Id, out var enrollment);
                var progressTowardNext = 0;
                if (enrolled && enrollment is not null)
                {
                    var totalQualifying = await _rewards.CountQualifyingPurchases(
                        _tenantContext.TenantId, userId, p.RequirementKind, enrollment.EnrolledAt);
                    var earned = (await _rewards.ListRedemptionsForProgram(p.Id))
                        .Count(r => r.UserId == userId);
                    progressTowardNext = Math.Max(0, totalQualifying - (earned * p.RequirementCount));
                }
                responses.Add(new RiderRewardProgramResponse
                {
                    ProgramId = p.Id,
                    Name = p.Name,
                    Description = p.Description,
                    EnrollmentMode = p.EnrollmentMode,
                    RequirementKind = p.RequirementKind,
                    RequirementCount = p.RequirementCount,
                    RewardPercentOff = p.RewardPercentOff,
                    IsEnrolled = enrolled,
                    Progress = progressTowardNext,
                    RemainingForReward = Math.Max(0, p.RequirementCount - progressTowardNext),
                    EnrolledAtUtc = enrollment is null ? null : DateTime.SpecifyKind(enrollment.EnrolledAt, DateTimeKind.Utc),
                });
            }

            return new ApiResponses().OkResult(responses);
        }

        [Authorize]
        [HttpPost("Programs/{id:guid}/Enroll")]
        public async Task<IActionResult> Enroll(Guid id)
        {
            if (!TryGetUserId(out var userId)) return new ApiResponses().BadRequestResult("Invalid token.");
            var program = await _rewards.GetProgram(id, _tenantContext.TenantId);
            if (program is null || !program.IsActive)
            {
                return new ApiResponses().NotFoundResult("Program not found.");
            }
            await _rewards.CreateEnrollment(id, userId);
            return new ApiResponses().OkResult();
        }

        [Authorize]
        [HttpPost("Programs/{id:guid}/Unenroll")]
        public async Task<IActionResult> Unenroll(Guid id)
        {
            if (!TryGetUserId(out var userId)) return new ApiResponses().BadRequestResult("Invalid token.");
            await _rewards.DeleteEnrollment(id, userId);
            return new ApiResponses().OkResult();
        }

        [Authorize(Policy = TenantPermissions.Policy.SalesCounter)]
        [HttpGet("Riders/{userId:guid}/Redemptions")]
        public async Task<IActionResult> ListRiderRedemptions(Guid userId)
        {
            return await RedemptionsForUser(userId);
        }

        [Authorize]
        [HttpGet("MyRedemptions")]
        public async Task<IActionResult> ListMyRedemptions()
        {
            if (!TryGetUserId(out var userId)) return new ApiResponses().BadRequestResult("Invalid token.");
            return await RedemptionsForUser(userId);
        }

        private async Task<IActionResult> RedemptionsForUser(Guid userId)
        {
            var redemptions = await _rewards.ListRedemptionsForUser(userId, unredeemedOnly: false);
            var programIds = redemptions.Select(r => r.ProgramId).Distinct().ToList();
            var nameByProgram = new Dictionary<Guid, RewardProgram>();
            foreach (var pid in programIds)
            {
                var p = await _rewards.GetProgram(pid, _tenantContext.TenantId);
                if (p is not null) nameByProgram[pid] = p;
            }

            var items = redemptions
                .Where(r => nameByProgram.ContainsKey(r.ProgramId))    // scope to this tenant
                .Select(r => new RiderRewardRedemption
                {
                    Id = r.Id,
                    ProgramId = r.ProgramId,
                    ProgramName = nameByProgram[r.ProgramId].Name,
                    RewardPercentOff = nameByProgram[r.ProgramId].RewardPercentOff,
                    EarnedAtUtc = DateTime.SpecifyKind(r.EarnedAt, DateTimeKind.Utc),
                    RedeemedAtUtc = r.RedeemedAt is null ? null : DateTime.SpecifyKind(r.RedeemedAt.Value, DateTimeKind.Utc),
                });
            return new ApiResponses().OkResult(items);
        }

        private bool TryGetUserId(out Guid userId)
        {
            var claim = User.FindFirst("UserId")?.Value;
            return Guid.TryParse(claim, out userId);
        }

        private static RewardProgramResponse ToResponse(RewardProgram p) => new()
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description,
            EnrollmentMode = p.EnrollmentMode,
            RequirementKind = p.RequirementKind,
            RequirementCount = p.RequirementCount,
            RewardPercentOff = p.RewardPercentOff,
            ProximityEmailThreshold = p.ProximityEmailThreshold,
            IsActive = p.IsActive,
            CreatedAtUtc = DateTime.SpecifyKind(p.CreatedAt, DateTimeKind.Utc),
        };
    }
}
