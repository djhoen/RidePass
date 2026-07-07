using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Services.Helpers;
using Services.Payments;
using Services.Repositories;
using Services.Scheduling;
using Services.Scheduling.Handlers;
using Services.Sms;

// RidePass TaskRunner, background process for deferred work.
//
// Two concurrent loops, different cadences:
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

// Three independent loops. Cancellation token wired so Ctrl-C stops all.
var dispatcherLoop = Task.Run(() => DispatcherLoop(dispatcher, cts.Token));
var drafterLoop = Task.Run(() => DrafterLoop(drafter, cts.Token));
var smsBillingLoop = Task.Run(() => SmsBillingAttachLoop(smsBillingAttacher, cts.Token));

await Task.WhenAll(dispatcherLoop, drafterLoop, smsBillingLoop);

Console.WriteLine("TaskRunner stopped.");

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
