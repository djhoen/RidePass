using System.Security.Cryptography;
using System.Text;
using Services.Email;
using Services.Helpers;
using Services.Helpers.Interfaces;
using Services.Repositories.Data.NewsletterData;
using Services.Repositories.Data.PaymentData;
using Services.Repositories.Data.TenantData;
using Services.Repositories.Interfaces;
using webapi.Controllers;

namespace webapi.Workers
{
    /// <summary>
    /// Delivers drip campaigns. One tenant-spanning periodic job, following the payout-drafter
    /// precedent: per armed automation, per step, find the passes that came due and email them.
    ///
    /// Steps evaluate INDEPENDENTLY. There is no flow-state to advance, so a rider who becomes
    /// ineligible after step 1 simply never matches step 2, and a tick that dies halfway through
    /// re-sends nothing on the next one. That is the whole simplification the linear model buys.
    /// See docs/drip-campaigns.md §6.
    /// </summary>
    public class MarketingAutomationSweep : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<MarketingAutomationSweep> _logger;

        // Hourly, because the finest scheduling control a tenant has is a send window in whole
        // hours. A tighter tick would burn queries to deliver nothing sooner.
        private static readonly TimeSpan TickInterval = TimeSpan.FromHours(1);
        // Cap per (automation, step) per tick so one track's back catalogue can't monopolise a
        // tick or a mail relay's rate limit. The remainder goes out on the next one.
        private const int BatchSize = 200;

        public MarketingAutomationSweep(IServiceProvider services, ILogger<MarketingAutomationSweep> logger)
        {
            _services = services;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Let the app finish starting before the first sweep; a deploy restart should not
            // race a migration.
            try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
            catch (TaskCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try { await RunOnce(stoppingToken); }
                catch (Exception ex) { _logger.LogError(ex, "Marketing automation sweep tick failed"); }
                try { await Task.Delay(TickInterval, stoppingToken); }
                catch (TaskCanceledException) { return; }
            }
        }

        private async Task RunOnce(CancellationToken ct)
        {
            using var scope = _services.CreateScope();
            var sp = scope.ServiceProvider;
            var repo = sp.GetRequiredService<IMarketingAutomationRepository>();
            var emailer = sp.GetRequiredService<ISmtpEmailer>();
            var tenants = sp.GetRequiredService<ITenantRepository>();
            var tokens = sp.GetRequiredService<IEmailLinkTokens>();
            var ledger = sp.GetRequiredService<ITenantLedgerRepository>();
            var config = sp.GetRequiredService<IConfiguration>();

            if (!emailer.IsConfigured) return;   // ships dark until SMTP is set

            var automations = await repo.ListActiveAcrossTenants();
            if (automations.Count == 0) return;

            var tickStart = DateTime.UtcNow;
            var rootDomain = config["Tenant:RootDomain"] ?? config["App:RootDomain"] ?? "ridepass.io";

            foreach (var a in automations)
            {
                if (ct.IsCancellationRequested) return;
                try
                {
                    var tenant = await tenants.GetById(a.TenantId);
                    if (tenant is null) continue;

                    // Outside the window there is nothing to do: the send log is keyed on
                    // (step, subject) and nothing has been written, so the step simply comes due
                    // again on the next tick, inside the window.
                    if (!SendWindow.IsOpen(a.SendWindowStart, a.SendWindowEnd, tenant.Timezone, tickStart))
                    {
                        continue;
                    }

                    var sent = await RunAutomation(a, tenant, repo, emailer, tokens, rootDomain, ct);
                    if (sent > 0)
                    {
                        await Bill(repo, ledger, a, sent, tickStart);
                    }
                }
                catch (Exception ex)
                {
                    // One bad automation must not stop the others.
                    _logger.LogError(ex, "Automation {Id} failed during sweep", a.Id);
                }
            }
        }

        private async Task<int> RunAutomation(
            MarketingAutomation a, Tenant tenant, IMarketingAutomationRepository repo,
            ISmtpEmailer emailer, IEmailLinkTokens tokens, string rootDomain, CancellationToken ct)
        {
            var steps = await repo.ListSteps(a.Id, a.TenantId);
            if (steps.Count == 0) return 0;

            var fromProductId = AutomationController.FromProductId(a);
            var baseUrl = $"https://{tenant.Subdomain}.{rootDomain}";
            var sentCount = 0;

            foreach (var step in steps)
            {
                if (ct.IsCancellationRequested) return sentCount;

                var due = await repo.ListDuePassSubjects(a, step, fromProductId, BatchSize);
                if (due.Count == BatchSize)
                {
                    // Never let a cap look like "that was everyone".
                    _logger.LogInformation(
                        "Automation {Id} step {Step}: batch capped at {Cap}; the rest sends next tick.",
                        a.Id, step.StepOrder, BatchSize);
                }

                foreach (var subject in due)
                {
                    if (ct.IsCancellationRequested) return sentCount;

                    // Claim BEFORE sending. Two workers can both see this pass as due; the unique
                    // index makes exactly one of them the sender.
                    var sendId = await repo.RecordSend(new MarketingAutomationSend
                    {
                        TenantId = a.TenantId,
                        AutomationId = a.Id,
                        StepId = step.Id,
                        SubjectKind = "season_pass_purchase",
                        SubjectId = subject.PurchaseId,
                        Email = subject.Email,
                        Status = "sent",
                    });
                    if (sendId is null) continue;

                    var ok = false;
                    try
                    {
                        ok = await Send(a, step, subject, tenant, emailer, tokens, baseUrl);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Automation {Id} send to {Email} threw", a.Id, subject.Email);
                    }

                    if (ok) { sentCount++; }
                    else
                    {
                        // Correct the optimistic claim. The row STAYS, so a broken template is a
                        // single logged failure per rider rather than a retry every hour forever.
                        await repo.MarkSendFailed(sendId.Value, a.TenantId, "Email send failed");
                    }
                }
            }
            return sentCount;
        }

        private static async Task<bool> Send(
            MarketingAutomation a, MarketingAutomationStep step, AutomationPassSubject subject,
            Tenant tenant, ISmtpEmailer emailer, IEmailLinkTokens tokens, string baseUrl)
        {
            var values = AutomationMergeFields.For(subject, tenant.DisplayName, baseUrl);
            var subjectLine = AutomationMergeFields.Render(step.Subject, values, htmlEncode: false);
            var body = AutomationMergeFields.Render(step.BodyHtml, values, htmlEncode: true);

            // Same compliance furniture as a broadcast campaign: this is marketing mail and the
            // law does not care that it was automated.
            var enc = Uri.EscapeDataString(tokens.GenerateUnsubscribe(a.TenantId, subject.Email));
            var headers = new Dictionary<string, string>
            {
                ["List-Unsubscribe"] = $"<{baseUrl}/api/Unsubscribe?token={enc}>",
                ["List-Unsubscribe-Post"] = "List-Unsubscribe=One-Click",
                ["X-SMTPAPI"] = System.Text.Json.JsonSerializer.Serialize(
                    new { unique_args = new { tenant_id = a.TenantId } }),
            };
            var html = body + UnsubscribeFooter($"{baseUrl}/EmailUnsubscribe?token={enc}", tenant.DisplayName);

            return await emailer.Send(subject.Email, subjectLine, html, headers, TenantEmailIdentity.For(tenant));
        }

        /// <summary>
        /// Deduct the sends from the tenant's payout: a negative ledger entry, no separate charge.
        /// Cumulative monthly tiers, so the marginal cost depends on this month's prior volume
        /// across campaigns AND automations.
        /// </summary>
        private async Task Bill(
            IMarketingAutomationRepository repo, ITenantLedgerRepository ledger,
            MarketingAutomation a, int sent, DateTime tickStart)
        {
            var monthStart = new DateTime(tickStart.Year, tickStart.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            // This tick's send rows are already written, so subtract them back out: the tier is
            // priced from the volume BEFORE this batch, and the batch is the delta being charged.
            var monthToDate = Math.Max(0, await repo.CountSentEmailsInMonth(a.TenantId, monthStart) - sent);
            var chargeCents = EmailPricing.MarginalChargeCents(monthToDate, sent);
            if (chargeCents <= 0) return;

            try
            {
                await ledger.Insert(new TenantLedgerEntry
                {
                    TenantId = a.TenantId,
                    EntryKind = "email_charge",
                    SourceKind = "marketing_automation",
                    // uk_ledger_email_charge is unique on (tenant, source_kind, source_id), so the
                    // automation id alone would allow exactly one charge for its whole lifetime.
                    // Keyed per tick instead: this tick bills only what this tick just sent, and a
                    // repeat of the same tick sends nothing so bills nothing.
                    SourceId = TickSourceId(a.Id, tickStart),
                    OccurredAtUtc = tickStart,
                    GrossCents = -chargeCents,
                    StripeFeeCents = 0,
                    RidepassCutCents = 0,
                    NetToTenantCents = -chargeCents,
                    PaymentMethod = "stripe",
                    Memo = $"Automation \"{a.Name}\", {sent} sent",
                });
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
            {
                _logger.LogDebug("Email charge for automation {Id} at {Tick} already recorded.", a.Id, tickStart);
            }
        }

        /// <summary>Stable id for (automation, tick hour), so a retried tick cannot double-charge.</summary>
        private static Guid TickSourceId(Guid automationId, DateTime tickStart)
        {
            var key = $"{automationId:N}:{tickStart:yyyyMMddHH}";
            return new Guid(MD5.HashData(Encoding.UTF8.GetBytes(key)));
        }

        private static string UnsubscribeFooter(string url, string tenantName) =>
            $@"<hr style=""border:none;border-top:1px solid #e5e7eb;margin:24px 0 12px"">
<p style=""font-size:12px;color:#9ca3af"">You're receiving this because you bought a pass from {System.Net.WebUtility.HtmlEncode(tenantName)}.
<a href=""{url}"" style=""color:#9ca3af"">Unsubscribe</a>.</p>";
    }
}
