using System.Reflection;
using DbUp;
using Microsoft.Extensions.Configuration;

namespace RidePass.Migrator;

public class Program
{
    public static int Main(string[] args)
    {
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

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Default is not configured. Set it via appsettings, user-secrets, or environment variables.");

        // EnsureDatabase needs access to a master `postgres` database to create the target.
        // Managed providers (DO, RDS, etc.) don't expose one; their DBs are created out-of-band.
        if (environment == "Development")
        {
            EnsureDatabase.For.PostgresqlDatabase(connectionString);
        }

        var upgrader = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(Assembly.GetExecutingAssembly())
            .WithTransactionPerScript()
            .LogToConsole()
            .Build();

        var result = upgrader.PerformUpgrade();

        if (!result.Successful)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(result.Error);
            Console.ResetColor();
            return 1;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Migrations applied successfully.");
        Console.ResetColor();
        return 0;
    }
}
