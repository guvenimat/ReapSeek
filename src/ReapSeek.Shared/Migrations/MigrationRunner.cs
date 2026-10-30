using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ReapSeek.Shared.Migrations;

public sealed record Migration(string Version, string Sql);

public static partial class MigrationRunner
{
    const string ResourcePrefix = "Migrations.";
    const long LockKey = 7_315_002_001;

    public static IReadOnlyList<Migration> Embedded()
    {
        var assembly = typeof(MigrationRunner).Assembly;
        return assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.EndsWith(".sql", StringComparison.Ordinal))
            .Select(n =>
            {
                var version = n[ResourcePrefix.Length..^".sql".Length];
                if (!VersionPattern().IsMatch(version)) throw new InvalidOperationException($"Invalid migration name: {version}");
                using var reader = new StreamReader(assembly.GetManifestResourceStream(n)!);
                return new Migration(version, reader.ReadToEnd());
            })
            .OrderBy(m => m.Version, StringComparer.Ordinal)
            .ToList();
    }

    public static async Task<int> ApplyAsync(NpgsqlDataSource db, ILogger log, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await Execute(conn, null, $"SELECT pg_advisory_lock({LockKey})", ct);
        try
        {
            await Execute(conn, null, """
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    version text PRIMARY KEY,
                    applied_at timestamptz NOT NULL DEFAULT now()
                )
                """, ct);

            var applied = new HashSet<string>(StringComparer.Ordinal);
            await using (var cmd = new NpgsqlCommand("SELECT version FROM schema_migrations", conn))
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
                while (await reader.ReadAsync(ct)) applied.Add(reader.GetString(0));

            var count = 0;
            foreach (var migration in Embedded().Where(m => !applied.Contains(m.Version)))
            {
                await using var tx = await conn.BeginTransactionAsync(ct);
                await Execute(conn, tx, migration.Sql, ct);
                await using (var insert = new NpgsqlCommand("INSERT INTO schema_migrations (version) VALUES ($1)", conn, tx))
                {
                    insert.Parameters.Add(new NpgsqlParameter { Value = migration.Version });
                    await insert.ExecuteNonQueryAsync(ct);
                }
                await tx.CommitAsync(ct);
                log.LogInformation("Applied migration {Version}", migration.Version);
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
