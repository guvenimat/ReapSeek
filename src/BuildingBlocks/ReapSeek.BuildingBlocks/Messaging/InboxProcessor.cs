using System.Diagnostics;
using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using ReapSeek.Shared.Observability;

namespace ReapSeek.BuildingBlocks.Messaging;

public sealed record MessagingOptions(TimeSpan PollInterval);

public sealed class InboxRepository(NpgsqlDataSource db)
{
    public sealed record InboxRowRecord(long Id, string SourceModule, long SourceMessageId, string EventType, string HandlerName, string Payload,
        int Attempts, DateTime OccurredAt, string? TraceParent);
    public sealed record ClaimRecord(int BatchSize, DateTime LeaseUntil);
    public sealed record InboxIdRecord(long Id);
    public sealed record InboxFailRecord(long Id, string Error, DateTime? RetryAt);

    public async Task<List<InboxMessage>> ClaimAsync(MessagingModule module, int batchSize, DateTime leaseUntil, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<InboxRowRecord>($"""
            UPDATE {module.Inbox} SET available_at = @LeaseUntil, attempts = attempts + 1
            WHERE id IN (
                SELECT id FROM {module.Inbox}
                WHERE processed_at IS NULL AND failed_at IS NULL AND available_at <= now()
                ORDER BY id LIMIT @BatchSize
                FOR UPDATE SKIP LOCKED
            )
            RETURNING id, source_module, source_message_id, event_type, handler_name, payload::text AS payload, attempts, occurred_at, trace_parent
            """, new ClaimRecord(batchSize, leaseUntil));
        return rows.OrderBy(r => r.Id)
            .Select(r => new InboxMessage(r.Id, r.SourceModule, r.SourceMessageId, r.EventType, r.HandlerName, r.Payload, r.Attempts, r.OccurredAt, r.TraceParent))
            .ToList();
    }

    public async Task CompleteAsync(MessagingModule module, long id, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await conn.ExecuteAsync($"UPDATE {module.Inbox} SET processed_at = now(), last_error = NULL WHERE id = @Id", new InboxIdRecord(id));
    }

    public async Task FailAsync(MessagingModule module, long id, string error, DateTime? retryAt, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await conn.ExecuteAsync($"""
            WITH changed AS (
                UPDATE {module.Inbox} SET last_error = @Error,
                    available_at = COALESCE(@RetryAt, available_at),
                    failed_at = CASE WHEN @RetryAt IS NULL THEN now() END
                WHERE id = @Id
                RETURNING id, handler_name, failed_at)
            INSERT INTO {JobTables.Platform.Table} (type, payload)
            SELECT 'outbox_failed', jsonb_build_object('message_id', id, 'type', '{module.Name}/' || handler_name) FROM changed
            WHERE failed_at IS NOT NULL
            """, new InboxFailRecord(id, error, retryAt));
    }
}

public sealed class InboxProcessor(MessagingModule module, InboxRepository inbox, Subscriptions subscriptions, IServiceProvider services,
    MessagingOptions options, ILogger<InboxProcessor> log) : BackgroundService
{
    const int BatchSize = 50;

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
                log.LogWarning("Inbox poll of {Module} failed: {Message}", module.Name, ex.Message);
            }
            if (processed < BatchSize) await Task.Delay(options.PollInterval, ct);
        }
    }

    public async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        var messages = await inbox.ClaimAsync(module, BatchSize, DateTime.UtcNow + RetryPolicy.Lease, ct);
        foreach (var message in messages)
        {
            var subscription = subscriptions.Find(module, message.HandlerName);
            var tags = new TagList { { "module", module.Name }, { "handler", message.HandlerName } };
            using var activity = Telemetry.StartConsumer($"inbox {module.Name}/{message.HandlerName}", message.TraceParent);
            activity?.SetTag("messaging.event_type", message.EventType);
            activity?.SetTag("messaging.message_id", message.Id);
            try
            {
                if (subscription is null) throw new InvalidOperationException($"No handler {message.HandlerName} is registered in module {module.Name}");
                await subscription.InvokeAsync(services, message, ct);
                await inbox.CompleteAsync(module, message.Id, ct);
                tags.Add("outcome", "ok");
                MessagingTelemetry.InboxProcessed.Add(1, tags);
                MessagingTelemetry.InboxDelay.Record((DateTime.UtcNow - message.OccurredAt).TotalSeconds, new("module", module.Name), new("handler", message.HandlerName));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                activity?.AddException(ex);
                var retryAt = RetryPolicy.RetryAt(message.Attempts, DateTime.UtcNow, subscription?.MaxAttempts ?? RetryPolicy.MaxAttempts);
                log.LogWarning("Inbox message {Id} ({Module}/{Handler}) failed on attempt {Attempt}: {Message}{Final}",
                    message.Id, module.Name, message.HandlerName, message.Attempts, ex.Message, retryAt is null ? "; giving up" : "");
                await inbox.FailAsync(module, message.Id, RetryPolicy.Truncate(ex.ToString()), retryAt, ct);
                tags.Add("outcome", retryAt is null ? "gave_up" : "retry");
                MessagingTelemetry.InboxProcessed.Add(1, tags);
            }
        }
        return messages.Count;
    }
}
