using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Dapper;
using Npgsql;

namespace ReapSeek.Shared.Outbox;

public static class OutboxTypes
{
    public const string UserRegistered = "user_registered";
}

public sealed record UserRegistered(long UserId, string Email, string Name);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(UserRegistered))]
public sealed partial class OutboxJsonContext : JsonSerializerContext;

public static class OutboxWriter
{
    public static Task EnqueueAsync<T>(NpgsqlConnection conn, NpgsqlTransaction tx, string type, T payload, JsonTypeInfo<T> typeInfo) =>
        conn.ExecuteAsync("INSERT INTO outbox (type, payload) VALUES (@Type, @Payload::jsonb)",
            new { Type = type, Payload = JsonSerializer.Serialize(payload, typeInfo) }, tx);
}
