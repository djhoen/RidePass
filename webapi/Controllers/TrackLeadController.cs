using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Notifications;
using Services.Repositories.Data.LeadData;
using Services.Repositories.Interfaces;
using webapi.Controllers.API.Data.TrackLead;

namespace webapi.Controllers
{
    /// <summary>
    /// Track leads — public lead capture from the apex "For Tracks" marketing
    /// page. Platform-level: this endpoint runs on the apex domain (no tenant
    /// subdomain), so it does NOT touch ITenantContext. Each submission stores
    /// a row and fans a notification (in-app + email) out to every super admin.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class TrackLeadController : ControllerBase
    {
        private readonly ITrackLeadRepository _leads;
        private readonly INotificationService _notifications;

        public TrackLeadController(ITrackLeadRepository leads, INotificationService notifications)
        {
            _leads = leads;
            _notifications = notifications;
        }

        // Public submit — no auth, no tenant. Anyone on the apex marketing page
        // can submit; abuse is mitigated by capturing IP + user agent.
        [AllowAnonymous]
        [HttpPost]
        public async Task<IActionResult> Submit([FromBody] SubmitTrackLeadRequest req)
        {
            var lead = new TrackLead
            {
                ContactName = req.ContactName.Trim(),
                TrackName = req.TrackName.Trim(),
                Email = req.Email.Trim(),
                Phone = string.IsNullOrWhiteSpace(req.Phone) ? null : req.Phone.Trim(),
                Message = string.IsNullOrWhiteSpace(req.Message) ? null : req.Message.Trim(),
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = TruncateUserAgent(Request.Headers.UserAgent.ToString()),
            };
            var id = await _leads.Create(lead);

            // Notify every super admin so sales can follow up. The body carries
            // the full lead so the email is actionable without opening anything.
            var body = $"New track lead from {lead.TrackName}. "
                     + $"Contact: {lead.ContactName} ({lead.Email}"
                     + (lead.Phone is null ? "" : $", {lead.Phone}") + "). "
                     + (lead.Message is null ? "No message." : $"Message: {lead.Message}");
            await _notifications.EmitToSuperAdmins(
                kind: "track_lead",
                title: $"New track lead: {lead.TrackName}",
                body: body);

            return new ApiResponses().OkResult(new { id });
        }

        private static string? TruncateUserAgent(string? ua)
        {
            if (string.IsNullOrWhiteSpace(ua)) return null;
            return ua.Length > 500 ? ua.Substring(0, 500) : ua;
        }
    }
}
