using Dapper;
using Npgsql;

namespace ReapSeek.Worker.Maintenance;

public sealed class CleanupJob(NpgsqlDataSource db, IConfiguration cfg, TimeProvider clock, ILogger<CleanupJob> log) : BackgroundService
{
    public sealed record CutoffRecord(DateTime Now, DateTime TokenCutoff, DateTime OutboxCutoff, DateTime GuestCartCutoff);
    public sealed record CleanupCountsRecord(int Sessions, int Tokens, int Outbox, int GuestCarts);

    readonly TimeSpan _interval = TimeSpan.FromSeconds(cfg.GetValue("Cleanup:IntervalSeconds", 3600));
    readonly TimeSpan _outboxRetention = TimeSpan.FromDays(cfg.GetValue("Cleanup:OutboxRetentionDays", 30));
    readonly TimeSpan _guestCartRetention = TimeSpan.FromDays(cfg.GetValue("Cleanup:GuestCartRetentionDays", 30));
    static readonly TimeSpan TokenRetention = TimeSpan.FromDays(7);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var c = await RunOnceAsync(ct);
                if (c.Sessions + c.Tokens + c.Outbox + c.GuestCarts > 0)
                    log.LogInformation("Cleanup removed {Sessions} sessions, {Tokens} account tokens, {Outbox} outbox messages and {GuestCarts} guest carts",
                        c.Sessions, c.Tokens, c.Outbox, c.GuestCarts);
            }
            catch (NpgsqlException ex)
            {
                log.LogWarning("Cleanup failed: {Message}", ex.Message);
            }
            await Task.Delay(_interval, ct);
        }
    }

    public async Task<CleanupCountsRecord> RunOnceAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.QuerySingleAsync<CleanupCountsRecord>("""
            WITH s AS (DELETE FROM sessions WHERE expires_at < @Now RETURNING 1),
                 t AS (DELETE FROM account_tokens WHERE expires_at < @TokenCutoff RETURNING 1),
                 o AS (DELETE FROM outbox WHERE processed_at < @OutboxCutoff RETURNING 1),
                 g AS (DELETE FROM carts WHERE customer_id IS NULL AND updated_at < @GuestCartCutoff RETURNING 1)
            SELECT (SELECT count(*)::int FROM s) AS sessions, (SELECT count(*)::int FROM t) AS tokens,
                (SELECT count(*)::int FROM o) AS outbox, (SELECT count(*)::int FROM g) AS guest_carts
            """, new CutoffRecord(now, now - TokenRetention, now - _outboxRetention, now - _guestCartRetention));
    }
}
