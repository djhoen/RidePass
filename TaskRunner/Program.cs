using Microsoft.Extensions.Configuration;
using Services.Helpers;

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

var timer = new PeriodicTimer(TimeSpan.FromMinutes(30));

do
{
    try
    {
        Console.WriteLine($"[{DateTime.UtcNow}] Running scheduled tasks...");

        // TODO: Add your scheduled tasks here
        // Example: Update currency exchange rates
        // Example: Send pending notification emails
        // Example: Clean up expired sessions

        Console.WriteLine($"[{DateTime.UtcNow}] Scheduled tasks completed.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{DateTime.UtcNow}] Error running scheduled tasks: {ex.Message}");
    }
}
while (await timer.WaitForNextTickAsync());

public partial class Program { }
