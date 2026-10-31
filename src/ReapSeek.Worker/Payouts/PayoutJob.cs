using Npgsql;
using ReapSeek.Shared.Payouts;

namespace ReapSeek.Worker.Payouts;

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
