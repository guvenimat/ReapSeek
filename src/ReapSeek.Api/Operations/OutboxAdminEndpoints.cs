using Dapper;
using Npgsql;
using ReapSeek.Api.Identity;
using ReapSeek.Api.Infrastructure;

namespace ReapSeek.Api.Operations;

public sealed record OutboxTypeSummary(string Type, int Pending, int Retrying, int Failed, int Discarded, int ProcessedLastDay);
public sealed record OutboxMessageRow(
    long Id, string Type, string Payload, int Attempts, string? LastError, DateTime CreatedAt, DateTime AvailableAt, DateTime? FailedAt, DateTime? DiscardedAt);
public sealed record OutboxResponse(List<OutboxTypeSummary> Types, List<OutboxMessageRow> Messages);
public sealed record OutboxRetryRequest(string? Type);
public sealed record OutboxRetryResult(int Retried);

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

public static class OutboxAdminEndpoints
{
    public sealed record ListRecord(string? Type, DateTime Since);
    public sealed record MessageIdRecord(long Id);
    public sealed record TypeRecord(string? Type);

    public static void MapOutboxAdmin(this RouteGroupBuilder api)
    {
        var admin = api.MapGroup("/backoffice/outbox").RequireAdmin();

        admin.MapGet("/", async (string? state, string? type, NpgsqlDataSource db, CancellationToken ct) =>
        {
            var s = string.IsNullOrEmpty(state) ? OutboxStates.Failed : state;
            if (!OutboxStates.All.Contains(s)) return ApiResults.BadRequest("state must be failed, retrying, pending or discarded");
            await using var conn = await db.OpenConnectionAsync(ct);
            var args = new ListRecord(string.IsNullOrWhiteSpace(type) ? null : type.Trim(), DateTime.UtcNow.AddDays(-1));
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
                WHERE {OutboxStates.Filter(s)} AND (@Type::text IS NULL OR type = @Type)
                ORDER BY id DESC LIMIT 100
                """, args)).AsList();
            return ApiResults.Ok(new OutboxResponse(types, messages), AppJsonContext.Default.OutboxResponse);
        });

        admin.MapPost("/{id:long}/retry", async (long id, NpgsqlDataSource db, CancellationToken ct) =>
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            var changed = await conn.ExecuteAsync("""
                UPDATE outbox SET attempts = 0, failed_at = NULL, discarded_at = NULL, available_at = now()
                WHERE id = @Id AND failed_at IS NOT NULL AND processed_at IS NULL
                """, new MessageIdRecord(id));
            return changed == 1 ? Results.NoContent() : await Missing(conn, id);
        });

        admin.MapPost("/{id:long}/discard", async (long id, NpgsqlDataSource db, CancellationToken ct) =>
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            var changed = await conn.ExecuteAsync(
                "UPDATE outbox SET discarded_at = now() WHERE id = @Id AND failed_at IS NOT NULL AND discarded_at IS NULL", new MessageIdRecord(id));
            return changed == 1 ? Results.NoContent() : await Missing(conn, id);
        });

        admin.MapPost("/retry", async (OutboxRetryRequest req, NpgsqlDataSource db, CancellationToken ct) =>
        {
            await using var conn = await db.OpenConnectionAsync(ct);
            var retried = await conn.ExecuteAsync($"""
                UPDATE outbox SET attempts = 0, failed_at = NULL, available_at = now()
                WHERE {OutboxStates.Filter(OutboxStates.Failed)} AND (@Type::text IS NULL OR type = @Type)
                """, new TypeRecord(string.IsNullOrWhiteSpace(req.Type) ? null : req.Type.Trim()));
            return ApiResults.Ok(new OutboxRetryResult(retried), AppJsonContext.Default.OutboxRetryResult);
        });
    }

    static async Task<IResult> Missing(NpgsqlConnection conn, long id) =>
        await conn.ExecuteScalarAsync<long?>("SELECT id FROM outbox WHERE id = @Id", new MessageIdRecord(id)) is null
            ? ApiResults.NotFound("Message not found")
            : ApiResults.Conflict("The message is not in a state that allows this");
}
