using Dapper;
using Npgsql;
using ReapSeek.Modules.Identity;
using ReapSeek.Modules.Ordering;

using ReapSeek.Worker.Ordering;
using ReapSeek.Worker.Marketing;
using ReapSeek.Worker.Engagement;
using ReapSeek.Worker.Identity;
using ReapSeek.Worker.Platform.Analytics;
using ReapSeek.Worker.Notifications;
using ReapSeek.Worker.Outbox;
namespace ReapSeek.Worker.Platform.Maintenance;

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
        var identity = await IdentityMaintenance.PurgeAsync(conn, now, now - TokenRetention);
        var outbox = await conn.ExecuteAsync("DELETE FROM platform.outbox WHERE processed_at < @OutboxCutoff", new CutoffRecord(now, now, now - _outboxRetention, now));
        var carts = await CartMaintenance.PurgeGuestCartsAsync(conn, now - _guestCartRetention);
        return new CleanupCountsRecord(identity.Sessions, identity.Tokens, outbox, carts);
    }
}
