using Dapper;
using Npgsql;

namespace ReapSeek.Api.Operations;

public sealed record OutboxTypeSummary(string Type, int Pending, int Retrying, int Failed, int Discarded, int ProcessedLastDay);
public sealed record OutboxMessageRow(
    long Id, string Type, string Payload, int Attempts, string? LastError, DateTime CreatedAt, DateTime AvailableAt, DateTime? FailedAt, DateTime? DiscardedAt);
public sealed record OutboxResponse(List<OutboxTypeSummary> Types, List<OutboxMessageRow> Messages);
public sealed record OutboxRetryRequest(string? Type);
public sealed record OutboxRetryResult(int Retried);

public enum OutboxChange { Changed, NotFound, NotAllowed }

public static class OutboxStates
{
    public const string Failed = "failed";
    public const string Retrying = "retrying";
    public const string Pending = "pending";
    public const string Discarded = "discarded";
    public static readonly string[] All = [Failed, Retrying, Pending, Discarded];

    public static string Filter(string state) => state switch
    {
        Failed => "failed_at IS NOT NULL AND discarded_at IS NULL",
        Retrying => "processed_at IS NULL AND failed_at IS NULL AND attempts > 0 AND last_error IS NOT NULL",
        Pending => "processed_at IS NULL AND failed_at IS NULL",
        Discarded => "discarded_at IS NOT NULL",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };
}

public sealed class OutboxAdminRepository(NpgsqlDataSource db)
{
    public sealed record ListRecord(string? Type, DateTime Since);
    public sealed record MessageIdRecord(long Id);
    public sealed record TypeRecord(string? Type);

    public async Task<OutboxResponse> ListAsync(string state, string? type, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var args = new ListRecord(type, DateTime.UtcNow.AddDays(-1));
        var types = (await conn.QueryAsync<OutboxTypeSummary>($"""
            SELECT type,
                count(*) FILTER (WHERE {OutboxStates.Filter(OutboxStates.Pending)})::int AS pending,
                count(*) FILTER (WHERE {OutboxStates.Filter(OutboxStates.Retrying)})::int AS retrying,
                count(*) FILTER (WHERE {OutboxStates.Filter(OutboxStates.Failed)})::int AS failed,
                count(*) FILTER (WHERE {OutboxStates.Filter(OutboxStates.Discarded)})::int AS discarded,
                count(*) FILTER (WHERE processed_at >= @Since)::int AS processed_last_day
            FROM outbox
            WHERE processed_at IS NULL OR processed_at >= @Since
            GROUP BY type ORDER BY type
            """, args)).AsList();
        var messages = (await conn.QueryAsync<OutboxMessageRow>($"""
            SELECT id, type, payload::text AS payload, attempts, last_error, created_at, available_at, failed_at, discarded_at
            FROM outbox
            WHERE {OutboxStates.Filter(state)} AND (@Type::text IS NULL OR type = @Type)
            ORDER BY id DESC LIMIT 100
            """, args)).AsList();
        return new OutboxResponse(types, messages);
    }

    public async Task<OutboxChange> RetryAsync(long id, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var changed = await conn.ExecuteAsync("""
            UPDATE outbox SET attempts = 0, failed_at = NULL, discarded_at = NULL, available_at = now()
            WHERE id = @Id AND failed_at IS NOT NULL AND processed_at IS NULL
            """, new MessageIdRecord(id));
        return changed == 1 ? OutboxChange.Changed : await MissingAsync(conn, id);
    }

    public async Task<OutboxChange> DiscardAsync(long id, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var changed = await conn.ExecuteAsync(
            "UPDATE outbox SET discarded_at = now() WHERE id = @Id AND failed_at IS NOT NULL AND discarded_at IS NULL", new MessageIdRecord(id));
        return changed == 1 ? OutboxChange.Changed : await MissingAsync(conn, id);
    }

    public async Task<int> RetryFailedAsync(string? type, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.ExecuteAsync($"""
            UPDATE outbox SET attempts = 0, failed_at = NULL, available_at = now()
            WHERE {OutboxStates.Filter(OutboxStates.Failed)} AND (@Type::text IS NULL OR type = @Type)
            """, new TypeRecord(type));
    }

    static async Task<OutboxChange> MissingAsync(NpgsqlConnection conn, long id) =>
        await conn.ExecuteScalarAsync<long?>("SELECT id FROM outbox WHERE id = @Id", new MessageIdRecord(id)) is null
            ? OutboxChange.NotFound
            : OutboxChange.NotAllowed;
}
