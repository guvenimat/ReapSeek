using System.Text.Json.Serialization;
using ReapSeek.Api.Health;
using ReapSeek.Api.Operations;

namespace ReapSeek.Api.Infrastructure;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, List<string>>))]
[JsonSerializable(typeof(OutboxResponse))]
[JsonSerializable(typeof(OutboxRetryRequest))]
[JsonSerializable(typeof(OutboxRetryResult))]
public sealed partial class AppJsonContext : JsonSerializerContext;
