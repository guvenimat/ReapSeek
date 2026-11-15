using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ReapSeek.Shared.Migrations;

public sealed record Migration(string Module, string Version, string Sql);

public static partial class MigrationRunner
{
    const string ResourcePrefix = "Migrations/";
    const long LockKey = 7_315_002_001;

    static readonly string[] ModuleOrder = ["platform", "identity", "sellers", "catalog", "ordering", "finance", "engagement", "marketing"];

    public static IReadOnlyList<string> Modules => ModuleOrder;

    public static IReadOnlyList<Migration> Embedded()
    {
        var assembly = typeof(MigrationRunner).Assembly;
        return assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.EndsWith(".sql", StringComparison.Ordinal))
            .Select(n =>
            {
                var name = n[ResourcePrefix.Length..^".sql".Length];
                var parts = name.Split('/');
                if (parts.Length != 2 || !ModuleOrder.Contains(parts[0]) || !VersionPattern().IsMatch(parts[1]))
                    throw new InvalidOperationException($"Invalid migration name: {name}");
                using var reader = new StreamReader(assembly.GetManifestResourceStream(n)!);
                return new Migration(parts[0], parts[1], reader.ReadToEnd());
            })
            .OrderBy(m => Array.IndexOf(ModuleOrder, m.Module))
            .ThenBy(m => m.Version, StringComparer.Ordinal)
            .ToList();
    }

    public static async Task<int> ApplyAsync(NpgsqlDataSource db, ILogger log, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await Execute(conn, null, $"SELECT pg_advisory_lock({LockKey})", ct);
        try
        {
            await Execute(conn, null, """
                CREATE TABLE IF NOT EXISTS public.schema_migrations (
                    module text NOT NULL,
                    version text NOT NULL,
                    applied_at timestamptz NOT NULL DEFAULT now(),
                    PRIMARY KEY (module, version)
                )
                """, ct);

            var applied = new HashSet<(string, string)>();
            await using (var cmd = new NpgsqlCommand("SELECT module, version FROM public.schema_migrations", conn))
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
                while (await reader.ReadAsync(ct)) applied.Add((reader.GetString(0), reader.GetString(1)));

            var count = 0;
            foreach (var migration in Embedded().Where(m => !applied.Contains((m.Module, m.Version))))
            {
                await using var tx = await conn.BeginTransactionAsync(ct);
                await Execute(conn, tx, migration.Sql, ct);
                await using (var insert = new NpgsqlCommand("INSERT INTO public.schema_migrations (module, version) VALUES ($1, $2)", conn, tx))
                {
                    insert.Parameters.Add(new NpgsqlParameter { Value = migration.Module });
                    insert.Parameters.Add(new NpgsqlParameter { Value = migration.Version });
                    await insert.ExecuteNonQueryAsync(ct);
                }
                await tx.CommitAsync(ct);
                log.LogInformation("Applied migration {Module}/{Version}", migration.Module, migration.Version);
                count++;
            }
            return count;
        }
        finally
        {
            await Execute(conn, null, $"SELECT pg_advisory_unlock({LockKey})", CancellationToken.None);
        }
    }

    static async Task Execute(NpgsqlConnection conn, NpgsqlTransaction? tx, string sql, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    [GeneratedRegex("^[0-9]{4}_[a-z0-9_]+$")]
    private static partial Regex VersionPattern();
}
