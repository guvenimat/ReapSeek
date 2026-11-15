using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using ReapSeek.Shared.Observability;

namespace ReapSeek.BuildingBlocks.Messaging;

public sealed record OutboxEvent(MessagingModule Source, long Id, string EventType, string Payload, DateTime OccurredAt, string? TraceParent);

public interface IEventDelivery
{
    Task DeliverAsync(NpgsqlTransaction tx, IReadOnlyList<OutboxEvent> events, CancellationToken ct);
}

public sealed class InboxDelivery(Subscriptions subscriptions) : IEventDelivery
{
    public sealed record InboxInsertRecord(string SourceModule, long SourceMessageId, string EventType, string HandlerName, string Payload,
        DateTime OccurredAt, string? TraceParent);

    public async Task DeliverAsync(NpgsqlTransaction tx, IReadOnlyList<OutboxEvent> events, CancellationToken ct)
    {
        var conn = tx.Connection ?? throw new InvalidOperationException("The relay transaction has no connection");
        foreach (var e in events)
            foreach (var subscription in subscriptions.Of(e.Source, e.EventType))
                await conn.ExecuteAsync($"""
                    INSERT INTO {subscription.Subscriber.Inbox} (source_module, source_message_id, event_type, handler_name, payload, occurred_at, trace_parent)
                    VALUES (@SourceModule, @SourceMessageId, @EventType, @HandlerName, @Payload::jsonb, @OccurredAt, @TraceParent)
                    ON CONFLICT (source_module, source_message_id, handler_name) DO NOTHING
                    """, new InboxInsertRecord(e.Source.Name, e.Id, e.EventType, subscription.Handler, e.Payload, e.OccurredAt, e.TraceParent), tx);
    }
}

public sealed class RelayRepository(NpgsqlDataSource db, IEventDelivery delivery)
{
    public sealed record OutboxRowRecord(long Id, string EventType, string Payload, DateTime OccurredAt, string? TraceParent);
    public sealed record ClaimRecord(int BatchSize);
    public sealed record PublishedRecord(long[] Ids);

    public async Task<List<OutboxEvent>> RelayAsync(MessagingModule module, int batchSize, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var rows = await conn.QueryAsync<OutboxRowRecord>($"""
            SELECT id, event_type, payload::text AS payload, occurred_at, trace_parent FROM {module.Outbox}
            WHERE published_at IS NULL
            ORDER BY id LIMIT @BatchSize
            FOR UPDATE SKIP LOCKED
            """, new ClaimRecord(batchSize), tx);
        var events = rows.Select(r => new OutboxEvent(module, r.Id, r.EventType, r.Payload, r.OccurredAt, r.TraceParent)).OrderBy(e => e.Id).ToList();
        if (events.Count == 0) return events;
        await delivery.DeliverAsync(tx, events, ct);
        await conn.ExecuteAsync($"UPDATE {module.Outbox} SET published_at = now() WHERE id = ANY(@Ids)", new PublishedRecord(events.Select(e => e.Id).ToArray()), tx);
        await tx.CommitAsync(ct);
        return events;
    }
}

public sealed class MessageRelay(RelayRepository relay, MessagingOptions options, ILogger<MessageRelay> log) : BackgroundService
{
    const int BatchSize = 100;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var full = false;
            foreach (var module in MessagingModules.All)
            {
                try
                {
                    var events = await relay.RelayAsync(module, BatchSize, ct);
                    foreach (var e in events)
                    {
                        using var activity = Telemetry.StartConsumer($"relay {e.EventType}", e.TraceParent);
                        activity?.SetTag("messaging.source", module.Name);
                        activity?.SetTag("messaging.message_id", e.Id);
                        MessagingTelemetry.Relayed.Add(1, new("module", module.Name), new("event_type", e.EventType));
                    }
                    full |= events.Count == BatchSize;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.LogWarning("Relay of {Module} outbox failed: {Message}", module.Name, ex.Message);
                }
            }
            if (!full) await Task.Delay(options.PollInterval, ct);
        }
    }
}
