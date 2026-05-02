using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Notifications;
using Services.Repositories.Interfaces;

namespace webapi.Controllers
{
    /// <summary>
    /// User-scoped notification inbox. Each authenticated user only sees notifications
    /// addressed to them (super admins see super-admin notifications; tenant admins see theirs).
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class NotificationController : ControllerBase
    {
        private readonly INotificationRepository _notifications;
        private readonly INotificationPreferenceRepository _prefs;

        public NotificationController(
            INotificationRepository notifications,
            INotificationPreferenceRepository prefs)
        {
            _notifications = notifications;
            _prefs = prefs;
        }

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] int take = 50)
        {
            if (!TryGetUserId(out var userId)) return new ApiResponses().BadRequestResult("Invalid token.");
            var items = await _notifications.ListForUser(userId, Math.Clamp(take, 1, 200));
            return new ApiResponses().OkResult(items);
        }

        [HttpGet("UnreadCount")]
        public async Task<IActionResult> UnreadCount()
        {
            if (!TryGetUserId(out var userId)) return new ApiResponses().BadRequestResult("Invalid token.");
            var count = await _notifications.CountUnread(userId);
            return new ApiResponses().OkResult(new { count });
        }

        [HttpPost("{id:guid}/Read")]
        public async Task<IActionResult> MarkRead(Guid id)
        {
            if (!TryGetUserId(out var userId)) return new ApiResponses().BadRequestResult("Invalid token.");
            await _notifications.MarkRead(id, userId);
            return new ApiResponses().OkResult(new { id, isRead = true });
        }

        [HttpPost("ReadAll")]
        public async Task<IActionResult> MarkAllRead()
        {
            if (!TryGetUserId(out var userId)) return new ApiResponses().BadRequestResult("Invalid token.");
            await _notifications.MarkAllRead(userId);
            return new ApiResponses().OkResult(new { ok = true });
        }

        /// <summary>
        /// Catalog of kinds whose email delivery is user-configurable. Filtered by caller's role —
        /// today only super admins receive emails, so non-super-admins get an empty list.
        /// </summary>
        [HttpGet("Catalog")]
        public IActionResult GetCatalog()
        {
            var role = User.FindFirst("role")?.Value;
            return new ApiResponses().OkResult(NotificationKinds.ForRole(role));
        }

        [HttpGet("Preferences")]
        public async Task<IActionResult> GetPreferences()
        {
            if (!TryGetUserId(out var userId)) return new ApiResponses().BadRequestResult("Invalid token.");
            var prefs = await _prefs.ListForUser(userId);
            return new ApiResponses().OkResult(prefs);
        }

        public class UpdatePreferenceRequest
        {
            public bool EmailEnabled { get; set; }
        }

        [HttpPut("Preferences/{kind}")]
        public async Task<IActionResult> UpdatePreference(string kind, [FromBody] UpdatePreferenceRequest body)
        {
            if (!TryGetUserId(out var userId)) return new ApiResponses().BadRequestResult("Invalid token.");
            await _prefs.Upsert(userId, kind, body.EmailEnabled);
            return new ApiResponses().OkResult(new { kind, emailEnabled = body.EmailEnabled });
        }

        private bool TryGetUserId(out Guid userId)
        {
            var claim = User.FindFirst("UserId")?.Value;
            return Guid.TryParse(claim, out userId);
        }
    }
}
