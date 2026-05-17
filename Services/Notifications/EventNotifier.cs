using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Services.Helpers;
using Services.Repositories.Data.EventData;
using Services.Repositories.Interfaces;

namespace Services.Notifications
{
    public class EventNotifier : IEventNotifier
    {
        private readonly IEventSubscriptionRepository _subs;
        private readonly ITenantRepository _tenants;
        private readonly ISmtpEmailer _emailer;
        private readonly ISmsSender _sms;
        private readonly IConfiguration _config;
        private readonly ILogger<EventNotifier> _logger;

        public EventNotifier(
            IEventSubscriptionRepository subs,
            ITenantRepository tenants,
            ISmtpEmailer emailer,
            ISmsSender sms,
            IConfiguration config,
            ILogger<EventNotifier> logger)
        {
            _subs = subs;
            _tenants = tenants;
            _emailer = emailer;
            _sms = sms;
            _config = config;
            _logger = logger;
        }

        public async Task NotifyNewEvent(Guid tenantId, Event ev)
        {
            var tenant = await _tenants.GetById(tenantId);
            if (tenant is null) return;

            // Tenant-level kill switch: existing subscriptions stay on the books but get
            // nothing while the setting is off. Flipping it back on resumes service.
            if (!tenant.AllowEventSubscriptions) return;

            var subs = await _subs.ListActiveForTenant(tenantId);
            if (subs.Count == 0) return;

            var apex = _config["App:RootDomain"] ?? "ridepass.io";
            var calendarUrl = $"https://{tenant.Subdomain}.{apex}/Calendar";
            var when = ev.AllDay
                ? ev.StartsAt.ToString("ddd, MMM d, yyyy")
                : ev.StartsAt.ToString("ddd, MMM d, yyyy 'at' h:mm tt UTC");
            var smsBody = $"{tenant.DisplayName}: new event \"{ev.Title}\" on {when}. See {calendarUrl}";

            foreach (var sub in subs)
            {
                try
                {
                    if (sub.NotifyEmail && _emailer.IsConfigured)
                    {
                        var unsubUrl = $"https://{tenant.Subdomain}.{apex}/EventUnsubscribe/{sub.UnsubscribeToken}";
                        var html = $@"<p>{System.Net.WebUtility.HtmlEncode(tenant.DisplayName)} just published a new event.</p>
<p><strong>{System.Net.WebUtility.HtmlEncode(ev.Title)}</strong><br/>
{when}{(string.IsNullOrWhiteSpace(ev.LocationLabel) ? "" : " · " + System.Net.WebUtility.HtmlEncode(ev.LocationLabel))}</p>
{(string.IsNullOrWhiteSpace(ev.Description) ? "" : "<p>" + System.Net.WebUtility.HtmlEncode(ev.Description) + "</p>")}
<p><a href=""{calendarUrl}"">See it on the calendar</a></p>
<hr/>
<p style=""font-size: 11px; color: #666"">
You're receiving this because you subscribed to event updates from {System.Net.WebUtility.HtmlEncode(tenant.DisplayName)}.
<a href=""{unsubUrl}"">Unsubscribe</a>.
</p>";
                        await _emailer.Send(sub.Email, $"New event from {tenant.DisplayName}: {ev.Title}", html);
                    }
                    if (sub.NotifySms && _sms.IsConfigured && !string.IsNullOrWhiteSpace(sub.Phone))
                    {
                        await _sms.Send(sub.Phone, smsBody);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Event notification failed for subscriber {SubId}", sub.Id);
                }
            }
        }
    }
}
