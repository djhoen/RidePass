using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Services.Helpers;
using Services.Payments;
using Services.Repositories;

// RidePass TaskRunner - Background task runner for scheduled jobs
// Add your scheduled tasks here (e.g., email sending, data cleanup, currency updates)

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

var dbHelper = new DbHelper(configuration);

// Hand-wired services (TaskRunner doesn't use full DI).
var tenantRepo = new TenantRepository(dbHelper);
var payoutRepo = new TenantPayoutRepository(dbHelper);
var drafterLogger = NullLogger<MonthlyPayoutDrafter>.Instance;
var drafter = new MonthlyPayoutDrafter(tenantRepo, payoutRepo, drafterLogger);

var timer = new PeriodicTimer(TimeSpan.FromMinutes(30));

do
{
    try
    {
        Console.WriteLine($"[{DateTime.UtcNow}] Running scheduled tasks...");

        var summary = await drafter.Run();
        Console.WriteLine($"[{DateTime.UtcNow}] Payout drafter: drafted={summary.Drafted} skipped={summary.Skipped} voidedEmpty={summary.VoidedEmpty} totalNet=${summary.TotalNetCents / 100m:0.00}");

        Console.WriteLine($"[{DateTime.UtcNow}] Scheduled tasks completed.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{DateTime.UtcNow}] Error running scheduled tasks: {ex.Message}");
    }
}
while (await timer.WaitForNextTickAsync());

public partial class Program { }
