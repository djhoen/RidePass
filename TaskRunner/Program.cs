using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Services.Helpers;
using Services.Payments;
using Services.QuickBooks;
using Services.Repositories;
using Services.Scheduling;
using Services.Scheduling.Handlers;
using Services.Sms;

// RidePass TaskRunner, background process for deferred work.
//
// Four concurrent loops, different cadences:
//
//   • Scheduled-task dispatcher (60s tick) — polls scheduled_task for due rows,
//     dispatches to per-kind handlers, retries with backoff on failure. The
//     intended home for every deferred job other than the monthly drafter.
//     Add a new handler: implement IScheduledTaskHandler, append to the
//     handlers list below, ship a migration adding rows of that kind.
//
//   • Monthly payout drafter (30m tick) — tenant-spanning sweep that drafts
//     the previous month's payout for any tenant that doesn't already have
//     one. Stays standalone because it's a single periodic sweep, not a
//     per-row job.
//
//   • QuickBooks sync (60m tick) — tenant-spanning sweep that posts each
//     connected tenant's completed local business days into their QuickBooks
//     Online company as one summarised journal entry per day. Idempotent via
//     the unique index on qbo_sync_log (tenant_id, business_date), so a tick
//     that overlaps a manual "Sync now" can't double-post. Hourly rather than
//     daily because tenants span timezones — a day closes at a different UTC
//     moment for each, and the sweep simply posts whatever is now complete.
//
//   • Staff alert tripwires (60m tick) — tenant-spanning sweep that runs each
//     tenant's completed local day of audit_log entries through the tripwire
//     rules and emails the owner anything that trips. Hourly rather than daily
//     because a local day closes at a different UTC moment per tenant; the
//     unique index on staff_alert_scan (tenant_id, scan_date) makes the repeat
//     safe and stops a day being emailed twice.
//
//   • SMS billing attacher (60s tick) — drains tenant_billing_event rows
//     into tenant_ledger_entry as negative sms_charge adjustments so the
//     monthly drafter rolls SMS costs into total_adjustment_cents. Same
//     "single periodic sweep" shape as the drafter, just faster cadence so
//     tenants see SMS deductions reflected on their balance promptly.

var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";

var configBuilder = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables();

if (environment == "Development")
{
    configBuilder.AddUserSecrets<Program>(optional: true);
}

IConfiguration configuration = configBuilder.Build();

// At-rest encryption for sensitive blobs the handlers may touch (e.g. tenant
// Twilio tokens via SmsSender). Fail fast at startup if not configured.
{
    var keyB64 = configuration["Encryption:KeyBase64"];
    var ivB64 = configuration["Encryption:IvBase64"];
    if (string.IsNullOrWhiteSpace(keyB64) || string.IsNullOrWhiteSpace(ivB64))
    {
        throw new InvalidOperationException(
            "Encryption:KeyBase64 and Encryption:IvBase64 must be configured " +
            "(use `dotnet user-secrets` in dev, env vars in prod).");
    }
    EncryptionHelper.Configure(Convert.FromBase64String(keyB64), Convert.FromBase64String(ivB64));
}

Console.WriteLine("TaskRunner started...");

// Hand-wired services — TaskRunner doesn't use the full DI container so it
// can stay a tiny console app with no ASP.NET host overhead.
var dbHelper = new DbHelper(configuration);

// Monthly payout drafter dependencies.
var tenantRepo = new TenantRepository(dbHelper);
var payoutRepo = new TenantPayoutRepository(dbHelper);
var drafter = new MonthlyPayoutDrafter(tenantRepo, payoutRepo,
    NullLogger<MonthlyPayoutDrafter>.Instance);

// Scheduled-task dispatcher dependencies.
var scheduledTaskRepo = new ScheduledTaskRepository(dbHelper);
var reportsRepo = new ReportsRepository(dbHelper);
var eventRepo = new EventRepository(dbHelper);
var conversationRepo = new TenantConversationRepository(dbHelper);
var smsOptOutRepo = new TenantSmsOptOutRepository(dbHelper);
var sms = new TwilioSmsSender(configuration, conversationRepo, smsOptOutRepo,
    NullLogger<TwilioSmsSender>.Instance);
var emailer = new SmtpEmailer(configuration, NullLogger<SmtpEmailer>.Instance);
var suppressionRepo = new EmailSuppressionRepository(dbHelper);
var emailLinkTokens = new EmailLinkTokens(configuration);
var campaignRepo = new EmailCampaignRepository(dbHelper);

// SMS billing attacher dependencies.
var billingEventRepo = new TenantBillingEventRepository(dbHelper);
var ledgerRepo = new TenantLedgerRepository(dbHelper);
var smsBillingAttacher = new SmsBillingPayoutAttacher(billingEventRepo, ledgerRepo,
    NullLogger<SmsBillingPayoutAttacher>.Instance);

// QuickBooks sync dependencies. Same "single periodic sweep" shape as the payout
// drafter, so it stays standalone rather than becoming a scheduled_task kind.
var quickBooksRepo = new QuickBooksRepository(dbHelper);
var accountingEntryRepo = new AccountingEntryRepository(dbHelper);
var quickBooksOptions = new QuickBooksOptions(configuration);
var quickBooksTokens = new QuickBooksTokenService(quickBooksOptions, quickBooksRepo, dbHelper,
    NullLogger<QuickBooksTokenService>.Instance);
var quickBooksApi = new QuickBooksApiClient(quickBooksOptions, quickBooksTokens, quickBooksRepo,
    NullLogger<QuickBooksApiClient>.Instance);
var quickBooksSync = new QuickBooksSyncService(quickBooksRepo, accountingEntryRepo, quickBooksApi,
    tenantRepo, NullLogger<QuickBooksSyncService>.Instance);

// Staff alert tripwires. Tenant-spanning sweep, same shape as the QuickBooks sync
// and for the same reason: a tenant's local day closes at a different UTC moment for
// each one, so it runs hourly and scans whatever has now finished, with the unique
// index on staff_alert_scan making the repeat safe.
var staffAlertSweep = new Services.Alerts.StaffAlertSweep(
    tenantRepo, new AuditLogRepository(dbHelper), new StaffAlertScanRepository(dbHelper), emailer);

// One entry per handler kind. Add new jobs here. These must mirror the
// IScheduledTaskHandler registrations in webapi/Program.cs — TaskRunner is the
// only process that actually runs the dispatcher, so a handler missing here
// means its task kind fails terminally ("No handler registered for kind ...").
var handlers = new IScheduledTaskHandler[]
{
    new SendRiderMessageHandler(reportsRepo, eventRepo, tenantRepo, sms, emailer,
        suppressionRepo, emailLinkTokens, configuration,
        NullLogger<SendRiderMessageHandler>.Instance),
    new SendCampaignHandler(campaignRepo, emailer, suppressionRepo, emailLinkTokens,
        tenantRepo, ledgerRepo, configuration,
        NullLogger<SendCampaignHandler>.Instance),
};
var dispatcher = new ScheduledTaskDispatcher(scheduledTaskRepo, handlers,
    NullLogger<ScheduledTaskDispatcher>.Instance);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

// Five independent loops. Cancellation token wired so Ctrl-C stops all.
var dispatcherLoop = Task.Run(() => DispatcherLoop(dispatcher, cts.Token));
var drafterLoop = Task.Run(() => DrafterLoop(drafter, cts.Token));
var smsBillingLoop = Task.Run(() => SmsBillingAttachLoop(smsBillingAttacher, cts.Token));
var quickBooksLoop = Task.Run(() => QuickBooksSyncLoop(quickBooksSync, quickBooksOptions, cts.Token));
var staffAlertLoop = Task.Run(() => StaffAlertLoop(staffAlertSweep, cts.Token));

await Task.WhenAll(dispatcherLoop, drafterLoop, smsBillingLoop, quickBooksLoop, staffAlertLoop);

Console.WriteLine("TaskRunner stopped.");

static async Task StaffAlertLoop(Services.Alerts.StaffAlertSweep sweep, CancellationToken ct)
{
    // Hourly for the same reason as the QuickBooks sync: tenants span timezones, so each one's
    // local day closes at a different UTC moment and the sweep just scans whatever has finished.
    // Re-running is safe (unique index on staff_alert_scan), so a restart costs nothing.
    var timer = new PeriodicTimer(TimeSpan.FromMinutes(60));
    try
    {
        do
        {
            try
            {
                var summary = await sweep.ScanDueTenantsAsync(ct);
                if (summary.DaysScanned > 0 || summary.Failures > 0)
                {
                    Console.WriteLine($"[{DateTime.UtcNow:o}] Staff alerts: tenants={summary.TenantsConsidered} "
                        + $"scanned={summary.DaysScanned} flagged={summary.DaysFlagged} "
                        + $"sent={summary.EmailsSent} failed={summary.Failures}");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[{DateTime.UtcNow:o}] Staff alert loop error: {ex.Message}");
            }
        }
        while (await timer.WaitForNextTickAsync(ct));
    }
    catch (OperationCanceledException) { /* shutting down */ }
}

static async Task QuickBooksSyncLoop(IQuickBooksSyncService sync, QuickBooksOptions options, CancellationToken ct)
{
    if (!options.IsConfigured)
    {
        // No Intuit app credentials on this deployment, so no tenant can be connected. Log once
        // and stop rather than tick hourly forever doing nothing.
        Console.WriteLine($"[{DateTime.UtcNow:o}] QuickBooks sync: not configured (QuickBooks:ClientId/ClientSecret/RedirectUri unset); loop disabled.");
        return;
    }

    var tick = TimeSpan.FromMinutes(60);
    var timer = new PeriodicTimer(tick);
    try
    {
        do
        {
            try
            {
                var summary = await sync.SyncDueTenantsAsync(ct);
                if (summary.TenantsConsidered > 0)
                {
                    Console.WriteLine($"[{DateTime.UtcNow:o}] QuickBooks sync: tenants={summary.TenantsConsidered} posted={summary.DaysPosted} skipped={summary.DaysSkipped} failed={summary.DaysFailed}");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[{DateTime.UtcNow:o}] QuickBooks sync loop error: {ex.Message}");
            }
        }
        while (await timer.WaitForNextTickAsync(ct));
    }
    catch (OperationCanceledException) { /* shutting down */ }
}

static async Task DispatcherLoop(ScheduledTaskDispatcher dispatcher, CancellationToken ct)
{
    var tick = TimeSpan.FromSeconds(60);
    var timer = new PeriodicTimer(tick);
    try
    {
        do
        {
            try
            {
                var processed = await dispatcher.RunOnce(batchSize: 25, ct);
                if (processed > 0)
                {
                    Console.WriteLine($"[{DateTime.UtcNow:o}] Dispatched {processed} scheduled task(s).");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[{DateTime.UtcNow:o}] Dispatcher loop error: {ex.Message}");
            }
        }
        while (await timer.WaitForNextTickAsync(ct));
    }
    catch (OperationCanceledException) { /* shutting down */ }
}

static async Task DrafterLoop(MonthlyPayoutDrafter drafter, CancellationToken ct)
{
    var tick = TimeSpan.FromMinutes(30);
    var timer = new PeriodicTimer(tick);
    try
    {
        do
        {
            try
            {
                var summary = await drafter.Run();
                Console.WriteLine($"[{DateTime.UtcNow:o}] Payout drafter: drafted={summary.Drafted} skipped={summary.Skipped} voidedEmpty={summary.VoidedEmpty} totalNet=${summary.TotalNetCents / 100m:0.00}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[{DateTime.UtcNow:o}] Drafter loop error: {ex.Message}");
            }
        }
        while (await timer.WaitForNextTickAsync(ct));
    }
    catch (OperationCanceledException) { /* shutting down */ }
}

static async Task SmsBillingAttachLoop(SmsBillingPayoutAttacher attacher, CancellationToken ct)
{
    var tick = TimeSpan.FromSeconds(60);
    var timer = new PeriodicTimer(tick);
    try
    {
        do
        {
            try
            {
                var summary = await attacher.Run(batchSize: 50, ct);
                if (summary.Attached > 0 || summary.Failed > 0)
                {
                    Console.WriteLine($"[{DateTime.UtcNow:o}] SMS billing attacher: attached={summary.Attached} failed={summary.Failed}");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[{DateTime.UtcNow:o}] SMS billing attach loop error: {ex.Message}");
            }
        }
        while (await timer.WaitForNextTickAsync(ct));
    }
    catch (OperationCanceledException) { /* shutting down */ }
}

public partial class Program { }
