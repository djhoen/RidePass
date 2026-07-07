using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Services.Helpers;
using Services.Helpers.Interfaces;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Data.ScheduledData;
using Services.Repositories.Interfaces;

namespace Services.Scheduling.Handlers
{
    public class SendCampaignPayload
    {
        public Guid CampaignId { get; set; }
    }

    /// <summary>
    /// Delivers a marketing email campaign in the background. The CampaignController
    /// materializes the (suppression-filtered) recipient rows as 'pending' and enqueues
    /// this task; here we send each pending row via SMTP with a List-Unsubscribe header,
    /// skip anyone who became suppressed since enqueue, and mark the campaign sent.
    /// Re-runnable: only 'pending' rows are sent, so a retry never double-sends.
    /// </summary>
    public class SendCampaignHandler : IScheduledTaskHandler
    {
        public string Kind => "send_campaign";

        private readonly IEmailCampaignRepository _campaigns;
        private readonly ISmtpEmailer _emailer;
        private readonly IEmailSuppressionRepository _suppression;
        private readonly IEmailLinkTokens _tokens;
        private readonly ITenantRepository _tenants;
        private readonly ITenantLedgerRepository _ledger;
        private readonly IConfiguration _config;
        private readonly ILogger<SendCampaignHandler> _logger;

        public SendCampaignHandler(
            IEmailCampaignRepository campaigns,
            ISmtpEmailer emailer,
            IEmailSuppressionRepository suppression,
            IEmailLinkTokens tokens,
            ITenantRepository tenants,
            ITenantLedgerRepository ledger,
            IConfiguration config,
            ILogger<SendCampaignHandler> logger)
        {
            _campaigns = campaigns;
            _emailer = emailer;
            _suppression = suppression;
            _tokens = tokens;
            _tenants = tenants;
            _ledger = ledger;
            _config = config;
            _logger = logger;
        }

        public async Task<ScheduledTaskOutcome> Execute(ScheduledTask task, CancellationToken ct)
        {
            SendCampaignPayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<SendCampaignPayload>(task.Payload,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException ex)
            {
                return ScheduledTaskOutcome.Fail($"Invalid payload: {ex.Message}");
            }
            if (payload is null || payload.CampaignId == Guid.Empty)
                return ScheduledTaskOutcome.Fail("Empty payload");

            if (!_emailer.IsConfigured)
                return ScheduledTaskOutcome.Fail("Email isn't configured (SMTP settings missing).");

            var tenant = await _tenants.GetById(task.TenantId);
            if (tenant is null) return ScheduledTaskOutcome.Fail("Tenant not found.");

            var campaign = await _campaigns.GetById(payload.CampaignId, task.TenantId);
            if (campaign is null) return ScheduledTaskOutcome.Fail("Campaign not found.");

            var rootDomain = _config["Tenant:RootDomain"] ?? "ridepass.io";
            var baseUrl = $"https://{tenant.Subdomain}.{rootDomain}";

            // Re-pull the blocklist at send time so opt-outs between enqueue and run are honored.
            var blocklist = await _suppression.ListMarketingBlocklist(task.TenantId);
            var sends = await _campaigns.ListSends(payload.CampaignId);

            int sent = 0, failed = 0, skipped = 0;
            foreach (var s in sends)
            {
                ct.ThrowIfCancellationRequested();
                if (s.Status != "pending") continue;   // retry-safe: never re-send a done row
                if (blocklist.Contains(s.Email))
                {
                    // Opt-out/bounce landed between enqueue and send. Use 'skipped' (a valid status);
                    // 'suppressed' is not in the email_campaign_send CHECK and would throw 23514 and
                    // abort the whole run. Record the reason so reporting still shows why.
                    await _campaigns.UpdateSendStatus(s.Id, "skipped", "Recipient suppressed (opt-out or bounce)");
                    s.Status = "skipped";
                    skipped++;
                    continue;
                }

                var enc = Uri.EscapeDataString(_tokens.GenerateUnsubscribe(task.TenantId, s.Email));
                var headers = new Dictionary<string, string>
                {
                    ["List-Unsubscribe"] = $"<{baseUrl}/api/Unsubscribe?token={enc}>",
                    ["List-Unsubscribe-Post"] = "List-Unsubscribe=One-Click",
                    // SendGrid copies unique_args onto every webhook event, so a spam report on this
                    // send scopes its suppression to this tenant. Other relays pass it through inertly.
                    ["X-SMTPAPI"] = JsonSerializer.Serialize(new { unique_args = new { tenant_id = task.TenantId } }),
                };
                var html = campaign.BodyHtml + UnsubscribeFooter($"{baseUrl}/EmailUnsubscribe?token={enc}", tenant.DisplayName);

                var ok = await _emailer.Send(s.Email, campaign.Subject, html, headers);
                await _campaigns.UpdateSendStatus(s.Id, ok ? "sent" : "failed", ok ? null : "SMTP send failed");
                s.Status = ok ? "sent" : "failed";
                if (ok) sent++; else failed++;
            }

            // Total delivered across all runs (sends loaded fresh each run reflects prior
            // runs' 'sent' rows), so MarkSent + billing are correct under retry.
            var totalSent = sends.Count(s => s.Status == "sent");
            await _campaigns.MarkSent(payload.CampaignId, totalSent);

            await BillSend(task.TenantId, payload.CampaignId, totalSent);

            var summary = $"Sent {sent}"
                + (failed > 0 ? $", {failed} failed" : "")
                + (skipped > 0 ? $", {skipped} suppressed" : "");
            _logger.LogInformation("Campaign {CampaignId}: {Summary}", payload.CampaignId, summary);
            return ScheduledTaskOutcome.Ok(summary);
        }

        // Deduct the send from the tenant's payout: a negative ledger entry, no separate Stripe
        // charge. Cumulative monthly tiers, so the marginal cost depends on this month's prior
        // volume. Idempotent — the partial unique index on (tenant, source_kind, source_id)
        // for entry_kind='email_charge' makes a retry's insert a no-op.
        private async Task BillSend(Guid tenantId, Guid campaignId, int totalSent)
        {
            if (totalSent <= 0) return;
            var now = DateTime.UtcNow;
            var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var monthToDate = await _campaigns.CountSentEmailsInMonth(tenantId, monthStart, campaignId);
            var chargeCents = EmailPricing.MarginalChargeCents(monthToDate, totalSent);
            if (chargeCents <= 0) return;
            try
            {
                await _ledger.Insert(new TenantLedgerEntry
                {
                    TenantId = tenantId,
                    EntryKind = "email_charge",
                    SourceKind = "email_campaign",
                    SourceId = campaignId,
                    OccurredAtUtc = now,
                    GrossCents = -chargeCents,
                    StripeFeeCents = 0,
                    RidepassCutCents = 0,
                    NetToTenantCents = -chargeCents,
                    PaymentMethod = "stripe",
                    Memo = $"Email campaign — {totalSent} sent",
                });
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
            {
                _logger.LogDebug("Email charge for campaign {Id} already recorded; skipping.", campaignId);
            }
        }

        // Visible unsubscribe link appended to the campaign HTML (CAN-SPAM requires a clear
        // opt-out in the body, in addition to the List-Unsubscribe header).
        private static string UnsubscribeFooter(string url, string tenantName) =>
            $@"<hr style=""border:none;border-top:1px solid #e5e7eb;margin:24px 0 12px"">
<p style=""font-size:12px;color:#9ca3af"">You're receiving this because you subscribed to updates from {WebUtility.HtmlEncode(tenantName)}.
<a href=""{url}"" style=""color:#9ca3af"">Unsubscribe</a>.</p>";
    }
}
