using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Services.Helpers;
using Services.Helpers.Interfaces;
using Services.Repositories.Data.ScheduledData;
using Services.Repositories.Interfaces;

namespace Services.Scheduling.Handlers
{
    /// <summary>
    /// Sends an SMS or email to a selected set of rider purchases for one event.
    /// Reuses the EventRiders projection so phone + email + name are already on
    /// the row — no per-purchase lookup needed. Tenant-scoped: only purchases
    /// in the task's tenant are touched.
    /// </summary>
    public class SendRiderMessageHandler : IScheduledTaskHandler
    {
        public string Kind => "send_rider_message";

        private readonly IReportsRepository _reports;
        private readonly IEventRepository _events;
        private readonly ITenantRepository _tenants;
        private readonly ISmsSender _sms;
        private readonly ISmtpEmailer _emailer;
        private readonly IEmailSuppressionRepository _suppression;
        private readonly IEmailLinkTokens _tokens;
        private readonly IConfiguration _config;
        private readonly ILogger<SendRiderMessageHandler> _logger;

        public SendRiderMessageHandler(
            IReportsRepository reports,
            IEventRepository events,
            ITenantRepository tenants,
            ISmsSender sms,
            ISmtpEmailer emailer,
            IEmailSuppressionRepository suppression,
            IEmailLinkTokens tokens,
            IConfiguration config,
            ILogger<SendRiderMessageHandler> logger)
        {
            _reports = reports;
            _events = events;
            _tenants = tenants;
            _sms = sms;
            _emailer = emailer;
            _suppression = suppression;
            _tokens = tokens;
            _config = config;
            _logger = logger;
        }

        public async Task<ScheduledTaskOutcome> Execute(ScheduledTask task, CancellationToken ct)
        {
            SendRiderMessagePayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<SendRiderMessagePayload>(task.Payload,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException ex)
            {
                return ScheduledTaskOutcome.Fail($"Invalid payload: {ex.Message}");
            }
            if (payload is null) return ScheduledTaskOutcome.Fail("Empty payload");
            if (payload.PurchaseIds.Count == 0) return ScheduledTaskOutcome.Fail("No recipients");
            if (string.IsNullOrWhiteSpace(payload.Body)) return ScheduledTaskOutcome.Fail("Empty body");

            var channel = (payload.Channel ?? "sms").ToLowerInvariant();
            if (channel != "sms" && channel != "email") return ScheduledTaskOutcome.Fail($"Unknown channel '{channel}'");

            var tenant = await _tenants.GetById(task.TenantId);
            if (tenant is null) return ScheduledTaskOutcome.Fail("Tenant not found.");

            // Channel-readiness check: if the relevant sender is not configured
            // there's no point retrying — fail terminally.
            if (channel == "sms" && !_sms.IsConfiguredFor(tenant))
                return ScheduledTaskOutcome.Fail("SMS isn't configured for this tenant.");
            if (channel == "email" && !_emailer.IsConfigured)
                return ScheduledTaskOutcome.Fail("Email isn't configured (SMTP settings missing).");

            var ev = await _events.GetById(payload.EventId, task.TenantId);
            if (ev is null) return ScheduledTaskOutcome.Fail($"Event {payload.EventId} not found in tenant.");

            // Base URL for the per-recipient unsubscribe link/header (email channel only).
            var rootDomain = _config["Tenant:RootDomain"] ?? "ridepass.io";
            var baseUrl = $"https://{tenant.Subdomain}.{rootDomain}";

            var rows = await _reports.GetEventRiders(task.TenantId, payload.EventId);
            var requested = payload.PurchaseIds.ToHashSet();
            var targets = rows.Where(r => requested.Contains(r.PurchaseId)).ToList();
            if (targets.Count == 0) return ScheduledTaskOutcome.Fail("No matching recipients on the event.");

            var sent = 0;
            var skipped = new List<string>();

            // Email blasts are admin-authored and can carry promotional content, so treat the
            // email channel as marketing: skip anyone on the suppression list (opt-outs +
            // complaints + hard bounces). Fetched once; SMS opt-outs are handled by Twilio.
            var blocklist = channel == "email"
                ? await _suppression.ListMarketingBlocklist(task.TenantId)
                : new HashSet<string>();

            foreach (var row in targets)
            {
                ct.ThrowIfCancellationRequested();
                bool ok;
                if (channel == "sms")
                {
                    var normalised = TwilioSmsSender.NormalizeE164(row.PurchaserPhone ?? "");
                    if (string.IsNullOrEmpty(normalised))
                    {
                        skipped.Add($"{row.PurchaserName} (no phone)");
                        continue;
                    }
                    ok = await _sms.Send(tenant, normalised, payload.Body);
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(row.PurchaserEmail))
                    {
                        skipped.Add($"{row.PurchaserName} (no email)");
                        continue;
                    }
                    if (blocklist.Contains(row.PurchaserEmail))
                    {
                        skipped.Add($"{row.PurchaserName} (unsubscribed)");
                        continue;
                    }
                    var subject = string.IsNullOrWhiteSpace(payload.Subject)
                        ? $"Update from {tenant.DisplayName}"
                        : payload.Subject!.Trim();
                    // This channel is treated as marketing (suppression-filtered above), so every
                    // send carries a one-click List-Unsubscribe header plus a visible footer link
                    // (CAN-SPAM + SES deliverability). Token is per-recipient.
                    var enc = Uri.EscapeDataString(_tokens.GenerateUnsubscribe(task.TenantId, row.PurchaserEmail));
                    var headers = new Dictionary<string, string>
                    {
                        ["List-Unsubscribe"] = $"<{baseUrl}/api/Unsubscribe?token={enc}>",
                        ["List-Unsubscribe-Post"] = "List-Unsubscribe=One-Click",
                    };
                    var html = BuildEmailBody(payload.Body, tenant.DisplayName, ev.Title,
                        $"{baseUrl}/EmailUnsubscribe?token={enc}");
                    ok = await _emailer.Send(row.PurchaserEmail, subject, html, headers, Services.Email.TenantEmailIdentity.For(tenant));
                }
                if (ok) sent++;
                else skipped.Add(row.PurchaserName);
            }

            var summary = skipped.Count == 0
                ? $"Sent {sent} ({channel})"
                : $"Sent {sent} ({channel}), skipped {skipped.Count}: {string.Join(", ", skipped.Take(5))}{(skipped.Count > 5 ? "…" : "")}";
            return ScheduledTaskOutcome.Ok(summary);
        }

        // Plain-text body wrapped in a minimal tenant-branded shell. Matches the
        // transactional-receipt shape elsewhere in the codebase. The admin types
        // plain text; we preserve their line breaks and escape any HTML so
        // pasting `<script>` etc. ships harmlessly as literal text.
        private static string BuildEmailBody(string plainBody, string tenantName, string eventTitle, string unsubscribeUrl)
        {
            var escaped = WebUtility.HtmlEncode(plainBody).Replace("\n", "<br>");
            return $@"<!doctype html>
<html><body style=""font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; max-width: 600px; margin: 0 auto; padding: 24px; color: #1f2937;"">
    <div style=""font-size: 12px; color: #6b7280; text-transform: uppercase; letter-spacing: 0.5px;"">{WebUtility.HtmlEncode(tenantName)}</div>
    <div style=""font-size: 18px; font-weight: 600; margin-top: 4px;"">{WebUtility.HtmlEncode(eventTitle)}</div>
    <hr style=""border: none; border-top: 1px solid #e5e7eb; margin: 16px 0;"">
    <div style=""font-size: 15px; line-height: 1.55;"">{escaped}</div>
    <hr style=""border: none; border-top: 1px solid #e5e7eb; margin: 24px 0 16px 0;"">
    <div style=""font-size: 12px; color: #9ca3af;"">Sent from {WebUtility.HtmlEncode(tenantName)}. Reply directly to reach the track.
    <br><a href=""{unsubscribeUrl}"" style=""color: #9ca3af;"">Unsubscribe</a> from these updates.</div>
</body></html>";
        }
    }
}
