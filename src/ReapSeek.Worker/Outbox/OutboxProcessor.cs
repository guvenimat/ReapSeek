using System.Diagnostics;
using Npgsql;
using ReapSeek.Shared.Observability;

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
        foreach (var group in messages.GroupBy(OutboxDispatcher.GroupOf))
        {
            var batch = group.ToList();
            using var activity = Telemetry.StartConsumer($"outbox {batch[0].Type}", batch[0].TraceParent);
            activity?.SetTag("outbox.type", batch[0].Type);
            activity?.SetTag("outbox.batch_size", batch.Count);
            activity?.SetTag("outbox.message_ids", string.Join(",", batch.Select(m => m.Id)));
            try
            {
                await dispatcher.HandleAsync(batch, ct);
                foreach (var message in batch)
                {
                    await outbox.CompleteAsync(message.Id, ct);
                    Telemetry.OutboxProcessed.Add(1, new("type", message.Type), new("outcome", "ok"));
                    Telemetry.OutboxDelay.Record((DateTime.UtcNow - message.CreatedAt).TotalSeconds, new KeyValuePair<string, object?>("type", message.Type));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                activity?.AddException(ex);
                foreach (var message in batch)
                {
                    var retryAt = OutboxPolicy.RetryAt(message.Attempts, DateTime.UtcNow);
                    log.LogWarning("Outbox message {Id} ({Type}) failed on attempt {Attempt}: {Message}{Final}",
                        message.Id, message.Type, message.Attempts, ex.Message, retryAt is null ? "; giving up" : "");
                    await outbox.FailAsync(message.Id, OutboxPolicy.Truncate(ex.ToString()), retryAt, ct);
                    Telemetry.OutboxProcessed.Add(1, new("type", message.Type), new("outcome", retryAt is null ? "gave_up" : "retry"));
                }
            }
        }
        return messages.Count;
    }
}
