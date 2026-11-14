using Npgsql;
using ReapSeek.Modules.Finance;

using ReapSeek.Worker.Ordering;
using ReapSeek.Worker.Marketing;
using ReapSeek.Worker.Engagement;
using ReapSeek.Worker.Identity;
using ReapSeek.Worker.Platform.Analytics;
using ReapSeek.Worker.Notifications;
using ReapSeek.Worker.Outbox;
namespace ReapSeek.Worker.Finance.Payouts;

public sealed class PayoutJob(PayoutRunner payouts, IConfiguration cfg, TimeProvider clock, ILogger<PayoutJob> log) : BackgroundService
{
    readonly TimeSpan _interval = TimeSpan.FromSeconds(cfg.GetValue("Payouts:CheckSeconds", 3600));

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await payouts.RunAsync(clock.GetUtcNow().UtcDateTime, ct);
            }
            catch (NpgsqlException ex)
            {
                log.LogWarning("Payout run failed: {Message}", ex.Message);
            }
            await Task.Delay(_interval, ct);
        }
    }
}
