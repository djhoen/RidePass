using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Instructor;
using webapi.Multitenancy;
using InstructorEntity = Services.Repositories.Data.InstructorData.Instructor;

namespace webapi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class InstructorController : ControllerBase
    {
        private readonly IInstructorRepository _instructors;
        private readonly ITenantContext _tenantContext;

        public InstructorController(IInstructorRepository instructors, ITenantContext tenantContext)
        {
            _instructors = instructors;
            _tenantContext = tenantContext;
        }

        // ── Public read: active instructors (for lesson detail / discovery) ────
        [HttpGet]
        public async Task<IActionResult> ListActive()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var rows = await _instructors.List(_tenantContext.TenantId, activeOnly: true);
            return new ApiResponses().OkResult(rows.Select(ToResponse));
        }

        // ── Admin CRUD ────────────────────────────────────────────────────────
        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpGet("Admin")]
        public async Task<IActionResult> ListForAdmin()
        {
            var rows = await _instructors.List(_tenantContext.TenantId, activeOnly: false);
            return new ApiResponses().OkResult(rows.Select(ToResponse));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] UpsertInstructorRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Name))
                return new ApiResponses().BadRequestResult("Instructor name is required.");
            var i = new InstructorEntity
            {
                TenantId = _tenantContext.TenantId,
                Name = req.Name.Trim(),
                Email = Clean(req.Email),
                Phone = Clean(req.Phone),
                Bio = Clean(req.Bio),
                ImageUrl = Clean(req.ImageUrl),
                IsActive = req.IsActive,
                SortOrder = req.SortOrder,
                MaxStudentsPerSession = req.MaxStudentsPerSession,
            };
            i.Id = await _instructors.Create(i);
            return new ApiResponses().OkResult(ToResponse(i));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpsertInstructorRequest req)
        {
            var existing = await _instructors.Get(id, _tenantContext.TenantId);
            if (existing is null) return new ApiResponses().NotFoundResult("Instructor not found.");
            if (string.IsNullOrWhiteSpace(req.Name))
                return new ApiResponses().BadRequestResult("Instructor name is required.");

            existing.Name = req.Name.Trim();
            existing.Email = Clean(req.Email);
            existing.Phone = Clean(req.Phone);
            existing.Bio = Clean(req.Bio);
            existing.ImageUrl = Clean(req.ImageUrl);
            existing.IsActive = req.IsActive;
            existing.SortOrder = req.SortOrder;
            existing.MaxStudentsPerSession = req.MaxStudentsPerSession;
            await _instructors.Update(existing);
            return new ApiResponses().OkResult(ToResponse(existing));
        }

        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var existing = await _instructors.Get(id, _tenantContext.TenantId);
            if (existing is null) return new ApiResponses().NotFoundResult("Instructor not found.");
            try
            {
                await _instructors.Delete(id, _tenantContext.TenantId);
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23503")
            {
                return new ApiResponses().BadRequestResult(
                    "This instructor is assigned to one or more lessons and can't be deleted. Set them inactive instead.");
            }
            return new ApiResponses().OkResult();
        }

        private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        private static InstructorResponse ToResponse(InstructorEntity i) => new()
        {
            Id = i.Id,
            Name = i.Name,
            Email = i.Email,
            Phone = i.Phone,
            Bio = i.Bio,
            ImageUrl = i.ImageUrl,
            IsActive = i.IsActive,
            SortOrder = i.SortOrder,
            MaxStudentsPerSession = i.MaxStudentsPerSession,
        };
    }
}
