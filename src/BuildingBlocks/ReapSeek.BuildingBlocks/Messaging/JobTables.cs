using Dapper;
using Npgsql;

namespace ReapSeek.BuildingBlocks.Messaging;

public sealed record JobTable(string Table, string TypeColumn, string CreatedColumn);

public static class JobTables
{
    public static readonly JobTable Platform = new("platform.outbox", "type", "created_at");

    public static readonly IReadOnlyList<JobTable> All =
        [Platform, .. MessagingModules.All.Select(m => new JobTable(m.Inbox, $"('{m.Name}/' || handler_name)", "received_at"))];

    public static readonly string Union = "(" + string.Join(" UNION ALL ", All.Select(t =>
        $"SELECT id, {t.TypeColumn} AS type, payload, attempts, last_error, {t.CreatedColumn} AS created_at, available_at, processed_at, failed_at, discarded_at FROM {t.Table}")) + ")";

    public static JobTable Of(long id) => MessagingModules.OfInboxId(id) is { } module ? All.Single(t => t.Table == module.Inbox) : Platform;
}

public static class MessagingMaintenance
{
    public sealed record PurgeRecord(DateTime Cutoff);

    public static async Task<int> PurgeAsync(NpgsqlConnection conn, DateTime cutoff)
    {
        var removed = 0;
        foreach (var module in MessagingModules.All)
        {
            removed += await conn.ExecuteAsync($"DELETE FROM {module.Outbox} WHERE published_at < @Cutoff", new PurgeRecord(cutoff));
            removed += await conn.ExecuteAsync($"DELETE FROM {module.Inbox} WHERE processed_at < @Cutoff", new PurgeRecord(cutoff));
        }
        return removed;
    }
}
