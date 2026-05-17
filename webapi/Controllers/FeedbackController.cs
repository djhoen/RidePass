using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Data.FeedbackData;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.Feedback;
using webapi.Helpers;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    /// <summary>
    /// Track Feedback — public submit + admin moderation. Anonymous (guest)
    /// submissions are accepted; the email + name on the row identify the
    /// submitter so admins can reply without joining to users.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class FeedbackController : ControllerBase
    {
        private readonly ITrackFeedbackRepository _feedback;
        private readonly ITenantContext _tenantContext;

        public FeedbackController(ITrackFeedbackRepository feedback, ITenantContext tenantContext)
        {
            _feedback = feedback;
            _tenantContext = tenantContext;
        }

        // Public submit — no auth. Tied to whichever tenant subdomain resolved.
        [HttpPost]
        public async Task<IActionResult> Submit([FromBody] SubmitFeedbackRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");

            // If the user is signed in, link the row to their account so admins can
            // see they're a known customer. Optional — guests still allowed.
            Guid? userId = Guid.TryParse(User.FindFirst("UserId")?.Value, out var uid) ? uid : (Guid?)null;
            var feedback = new TrackFeedback
            {
                TenantId = _tenantContext.TenantId,
                UserId = userId,
                Name = req.Name.Trim(),
                Email = req.Email.Trim(),
                Rating = req.Rating,
                Body = req.Body.Trim(),
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = TruncateUserAgent(Request.Headers.UserAgent.ToString()),
            };
            var id = await _feedback.Create(feedback);
            return new ApiResponses().OkResult(new { id });
        }

        // Admin list with optional status filter + simple pagination. The default
        // page is 25 rows; admins typically scan recent items first.
        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpGet("Admin")]
        public async Task<IActionResult> ListAdmin(
            [FromQuery] string? status = null,
            [FromQuery] int limit = 25,
            [FromQuery] int offset = 0)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            limit = Math.Clamp(limit, 1, 200);
            offset = Math.Max(0, offset);
            var statusFilter = status is "new" or "addressed" or "dismissed" ? status : null;
            var rows = await _feedback.ListByTenant(_tenantContext.TenantId, statusFilter, limit, offset);
            var total = await _feedback.CountByTenant(_tenantContext.TenantId, statusFilter);
            return new ApiResponses().OkResult(new FeedbackListResponse
            {
                Total = total,
                Items = rows.Select(ToResponse).ToList(),
            });
        }

        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpPut("Admin/{id:guid}/Status")]
        public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateFeedbackStatusRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!Guid.TryParse(User.FindFirst("UserId")?.Value, out var actionedBy))
            {
                return new ApiResponses().BadRequestResult("Invalid token.");
            }
            var existing = await _feedback.GetById(id, _tenantContext.TenantId);
            if (existing is null) return new ApiResponses().NotFoundResult("Feedback not found.");
            await _feedback.UpdateStatus(id, _tenantContext.TenantId, req.Status,
                string.IsNullOrWhiteSpace(req.AdminNotes) ? null : req.AdminNotes.Trim(),
                actionedBy);
            var refreshed = await _feedback.GetById(id, _tenantContext.TenantId);
            return new ApiResponses().OkResult(ToResponse(refreshed!));
        }

        private static FeedbackResponse ToResponse(TrackFeedback f) => new()
        {
            Id = f.Id,
            Name = f.Name,
            Email = f.Email,
            Rating = f.Rating,
            Body = f.Body,
            Status = f.Status,
            AdminNotes = f.AdminNotes,
            UserId = f.UserId,
            ActionedByUserId = f.ActionedByUserId,
            ActionedAtUtc = f.ActionedAtUtc.HasValue
                ? DateTime.SpecifyKind(f.ActionedAtUtc.Value, DateTimeKind.Utc)
                : null,
            CreatedAtUtc = DateTime.SpecifyKind(f.CreatedAt, DateTimeKind.Utc),
        };

        private static string? TruncateUserAgent(string? ua)
        {
            if (string.IsNullOrWhiteSpace(ua)) return null;
            return ua.Length > 500 ? ua.Substring(0, 500) : ua;
        }
    }
}
