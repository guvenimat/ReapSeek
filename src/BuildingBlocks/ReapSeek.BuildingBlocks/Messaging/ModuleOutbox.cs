using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Dapper;
using Npgsql;
using ReapSeek.Shared.Observability;

namespace ReapSeek.BuildingBlocks.Messaging;

public sealed class ModuleOutbox(MessagingModule module)
{
    public sealed record OutboxEventRecord(string EventType, string Payload, string? TraceParent);

    readonly string _insert = $"INSERT INTO {module.Outbox} (event_type, payload, trace_parent) VALUES (@EventType, @Payload::jsonb, @TraceParent)";

    public MessagingModule Module => module;

    public Task PublishAsync<T>(NpgsqlConnection conn, NpgsqlTransaction tx, string eventType, T payload, JsonTypeInfo<T> typeInfo) =>
        conn.ExecuteAsync(_insert, new OutboxEventRecord(eventType, JsonSerializer.Serialize(payload, typeInfo), Telemetry.CurrentTraceParent()), tx);
}
