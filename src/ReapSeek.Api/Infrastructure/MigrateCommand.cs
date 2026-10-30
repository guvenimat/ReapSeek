using Npgsql;
using ReapSeek.Api.Seed;
using ReapSeek.Shared;
using ReapSeek.Shared.Migrations;

namespace ReapSeek.Api.Infrastructure;

public static class MigrateCommand
{
    public const string Migrate = "--migrate";
    public const string Seed = "--seed";
    public const string Reset = "--reset";
    public const string Reindex = "--reindex";
    const int MaxAttempts = 30;
    static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    public static bool Handles(string[] args) => args.Contains(Migrate) || args.Contains(Seed) || args.Contains(Reindex);

    public static async Task<int> RunAsync(string[] args)
    {
        var flags = new[] { Migrate, Seed, Reset, Reindex };
        var cfg = new ConfigurationBuilder().AddEnvironmentVariables().AddCommandLine(args.Where(a => !flags.Contains(a)).ToArray()).Build();
        using var loggers = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true));
        var log = loggers.CreateLogger("ReapSeek");
        await using var db = Database.Create(cfg.GetConnectionString("Postgres"));

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var applied = await MigrationRunner.ApplyAsync(db, log, CancellationToken.None);
                log.LogInformation("Database is up to date ({Applied} migrations applied)", applied);
                if (args.Contains(Seed)) await CatalogSeeder.SeedAsync(db, args.Contains(Reset), log, CancellationToken.None);
                if (args.Contains(Reindex)) await CatalogSeeder.RequestReindexAsync(db, "manual", log, CancellationToken.None);
                return 0;
            }
            catch (NpgsqlException ex) when (ex is not PostgresException && attempt < MaxAttempts)
            {
                log.LogWarning("Database is not reachable ({Message}), retrying", ex.Message);
                await Task.Delay(RetryDelay);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Command failed");
                return 1;
            }
        }
    }
}
