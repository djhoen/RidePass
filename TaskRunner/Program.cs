using Services.Helpers;
using Services.Helpers.Interfaces;

// Template TaskRunner - Background task runner for scheduled jobs
// Add your scheduled tasks here (e.g., email sending, data cleanup, currency updates)

Console.WriteLine("TaskRunner started...");

var dbHelper = new DbHelper();

// Example: Run tasks on a timer
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
