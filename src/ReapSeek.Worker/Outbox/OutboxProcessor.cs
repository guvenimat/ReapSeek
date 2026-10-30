using Npgsql;

namespace ReapSeek.Worker.Outbox;

public sealed class OutboxProcessor(OutboxRepository outbox, OutboxDispatcher dispatcher, IConfiguration cfg, ILogger<OutboxProcessor> log)
    : BackgroundService
{
    const int BatchSize = 50;
    readonly TimeSpan _pollInterval = TimeSpan.FromMilliseconds(cfg.GetValue("Outbox:PollMilliseconds", 1000));

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var processed = 0;
            try
            {
                processed = await ProcessBatchAsync(ct);
            }
            catch (NpgsqlException ex)
            {
                log.LogWarning("Outbox poll failed: {Message}", ex.Message);
            }
            if (processed < BatchSize) await Task.Delay(_pollInterval, ct);
        }
    }

    async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        var messages = await outbox.ClaimAsync(BatchSize, DateTime.UtcNow + OutboxPolicy.Lease, ct);
        foreach (var message in messages)
        {
            try
            {
                await dispatcher.HandleAsync(message, ct);
                await outbox.CompleteAsync(message.Id, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var retryAt = OutboxPolicy.RetryAt(message.Attempts, DateTime.UtcNow);
                log.LogWarning("Outbox message {Id} ({Type}) failed on attempt {Attempt}: {Message}{Final}",
                    message.Id, message.Type, message.Attempts, ex.Message, retryAt is null ? "; giving up" : "");
                await outbox.FailAsync(message.Id, OutboxPolicy.Truncate(ex.ToString()), retryAt, ct);
            }
        }
        return messages.Count;
    }
}
