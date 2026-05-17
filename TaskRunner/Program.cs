using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Services.Helpers;
using Services.Payments;
using Services.Repositories;
using Services.Scheduling;
using Services.Scheduling.Handlers;

// RidePass TaskRunner — background process for deferred work.
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
var sms = new TwilioSmsSender(configuration, NullLogger<TwilioSmsSender>.Instance);
var emailer = new SmtpEmailer(configuration, NullLogger<SmtpEmailer>.Instance);

// One entry per handler kind. Add new jobs here.
var handlers = new IScheduledTaskHandler[]
{
    new SendRiderMessageHandler(reportsRepo, eventRepo, tenantRepo, sms, emailer,
        NullLogger<SendRiderMessageHandler>.Instance),
};
var dispatcher = new ScheduledTaskDispatcher(scheduledTaskRepo, handlers,
    NullLogger<ScheduledTaskDispatcher>.Instance);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

// Two independent loops. Cancellation token wired so Ctrl-C stops both.
var dispatcherLoop = Task.Run(() => DispatcherLoop(dispatcher, cts.Token));
var drafterLoop = Task.Run(() => DrafterLoop(drafter, cts.Token));

await Task.WhenAll(dispatcherLoop, drafterLoop);

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

public partial class Program { }
