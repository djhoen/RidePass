using Microsoft.Extensions.Logging;
using Services.Helpers;
using Services.Repositories.Data.NotificationData;
using Services.Repositories.Interfaces;

namespace Services.Notifications
{
    public interface INotificationService
    {
        /// <summary>
        /// Fan out a notification to every active super admin (one row per recipient + email if configured).
        /// </summary>
        Task EmitToSuperAdmins(string kind, string title, string body, string? linkUrl = null, Guid? tenantId = null);

        /// <summary>
        /// Fan out a notification to every active tenant_admin for the tenant (in-app only, no email).
        /// </summary>
        Task EmitToTenantAdmins(Guid tenantId, string kind, string title, string body, string? linkUrl = null);

        /// <summary>
        /// Fan out to every active user in the given tenant roles (in-app only). De-duplicated per user.
        /// </summary>
        Task EmitToTenantRoles(Guid tenantId, string[] roles, string kind, string title, string body, string? linkUrl = null);

        /// <summary>
        /// Direct notification to a single user (in-app only).
        /// </summary>
        Task EmitToUser(Guid userId, string kind, string title, string body, string? linkUrl = null, Guid? tenantId = null);
    }

    public class NotificationService : INotificationService
    {
        private readonly INotificationRepository _notifications;
        private readonly INotificationPreferenceRepository _prefs;
        private readonly IUserRepository _users;
        private readonly ISmtpEmailer _emailer;
        private readonly ILogger<NotificationService> _logger;

        public NotificationService(
            INotificationRepository notifications,
            INotificationPreferenceRepository prefs,
            IUserRepository users,
            ISmtpEmailer emailer,
            ILogger<NotificationService> logger)
        {
            _notifications = notifications;
            _prefs = prefs;
            _users = users;
            _emailer = emailer;
            _logger = logger;
        }

        public async Task EmitToSuperAdmins(string kind, string title, string body, string? linkUrl = null, Guid? tenantId = null)
        {
            var supers = await _users.ListSuperAdmins();
            foreach (var u in supers)
            {
                try
                {
                    await _notifications.Insert(new Notification
                    {
                        RecipientUserId = u.Id,
                        TenantId = tenantId,
                        Kind = kind,
                        Title = title,
                        Body = body,
                        LinkUrl = linkUrl,
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to insert notification for super admin {UserId}", u.Id);
                }

                if (_emailer.IsConfigured && await _prefs.IsEmailEnabled(u.Id, kind))
                {
                    var html = $"<p>{System.Net.WebUtility.HtmlEncode(body)}</p>"
                             + (linkUrl is null ? "" : $"<p><a href=\"{System.Net.WebUtility.HtmlEncode(linkUrl)}\">View</a></p>");
                    _ = _emailer.Send(u.Email, $"[RidePass] {title}", html);   // fire-and-forget
                }
            }
        }

        public async Task EmitToTenantAdmins(Guid tenantId, string kind, string title, string body, string? linkUrl = null)
        {
            var admins = await _users.ListTenantUsersByRole(tenantId, "tenant_admin");
            foreach (var u in admins)
            {
                try
                {
                    await _notifications.Insert(new Notification
                    {
                        RecipientUserId = u.Id,
                        TenantId = tenantId,
                        Kind = kind,
                        Title = title,
                        Body = body,
                        LinkUrl = linkUrl,
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to insert notification for tenant admin {UserId}", u.Id);
                }

                if (_emailer.IsConfigured && await _prefs.IsEmailEnabled(u.Id, kind))
                {
                    var html = $"<p>{System.Net.WebUtility.HtmlEncode(body)}</p>"
                             + (linkUrl is null ? "" : $"<p><a href=\"{System.Net.WebUtility.HtmlEncode(linkUrl)}\">View</a></p>");
                    _ = _emailer.Send(u.Email, $"[RidePass] {title}", html);   // fire-and-forget
                }
            }
        }

        public async Task EmitToTenantRoles(Guid tenantId, string[] roles, string kind, string title, string body, string? linkUrl = null)
        {
            var seen = new HashSet<Guid>();
            foreach (var role in roles)
            foreach (var u in await _users.ListTenantUsersByRole(tenantId, role))
            {
                if (!seen.Add(u.Id)) continue;   // a user could match more than one role
                try
                {
                    await _notifications.Insert(new Notification
                    {
                        RecipientUserId = u.Id,
                        TenantId = tenantId,
                        Kind = kind,
                        Title = title,
                        Body = body,
                        LinkUrl = linkUrl,
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to insert {Kind} notification for user {UserId}", kind, u.Id);
                }
            }
        }

        public async Task EmitToUser(Guid userId, string kind, string title, string body, string? linkUrl = null, Guid? tenantId = null)
        {
            try
            {
                await _notifications.Insert(new Notification
                {
                    RecipientUserId = userId,
                    TenantId = tenantId,
                    Kind = kind,
                    Title = title,
                    Body = body,
                    LinkUrl = linkUrl,
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to insert notification for user {UserId}", userId);
            }
        }
    }
}
