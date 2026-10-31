using Dapper;
using Npgsql;

namespace ReapSeek.Worker.Outbox;

public sealed class OutboxRepository(NpgsqlDataSource db)
{
    public sealed record MessageRecord(long Id, string Type, string Payload, int Attempts);

    public async Task<List<MessageRecord>> ClaimAsync(int batchSize, DateTime leaseUntil, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<MessageRecord>("""
            UPDATE outbox SET available_at = @LeaseUntil, attempts = attempts + 1
            WHERE id IN (
                SELECT id FROM outbox
                WHERE processed_at IS NULL AND failed_at IS NULL AND available_at <= now()
                ORDER BY id LIMIT @BatchSize
                FOR UPDATE SKIP LOCKED
            )
            RETURNING id, type, payload::text AS payload, attempts
            """, new { BatchSize = batchSize, LeaseUntil = leaseUntil });
        return rows.OrderBy(r => r.Id).ToList();
    }

    public async Task CompleteAsync(long id, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await conn.ExecuteAsync("UPDATE outbox SET processed_at = now(), last_error = NULL WHERE id = @Id", new { Id = id });
    }

    public async Task FailAsync(long id, string error, DateTime? retryAt, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await conn.ExecuteAsync("""
            WITH changed AS (
                UPDATE outbox SET last_error = @Error,
                    available_at = COALESCE(@RetryAt, available_at),
                    failed_at = CASE WHEN @RetryAt IS NULL THEN now() END
                WHERE id = @Id
                RETURNING id, type, failed_at)
            INSERT INTO outbox (type, payload)
            SELECT 'outbox_failed', jsonb_build_object('message_id', id, 'type', type) FROM changed
            WHERE failed_at IS NOT NULL AND type <> 'outbox_failed'
            """, new { Id = id, Error = error, RetryAt = retryAt });
    }
}
