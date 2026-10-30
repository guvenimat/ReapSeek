using Npgsql;
using ReapSeek.Shared;
using ReapSeek.Shared.Migrations;

namespace ReapSeek.Api.Infrastructure;

public static class MigrateCommand
{
    const int MaxAttempts = 30;
    static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    public static async Task<int> RunAsync(string[] args)
    {
        var cfg = new ConfigurationBuilder().AddEnvironmentVariables().AddCommandLine(args.Where(a => a != "--migrate").ToArray()).Build();
        using var loggers = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true));
        var log = loggers.CreateLogger("Migrations");
        await using var db = Database.Create(cfg.GetConnectionString("Postgres"));

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var applied = await MigrationRunner.ApplyAsync(db, log, CancellationToken.None);
                log.LogInformation("Database is up to date ({Applied} migrations applied)", applied);
                return 0;
            }
            catch (NpgsqlException ex) when (ex is not PostgresException && attempt < MaxAttempts)
            {
                log.LogWarning("Database is not reachable ({Message}), retrying", ex.Message);
                await Task.Delay(RetryDelay);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Migration failed");
                return 1;
            }
        }
    }
}
