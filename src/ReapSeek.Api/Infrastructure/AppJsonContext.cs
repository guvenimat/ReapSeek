using System.Text.Json.Serialization;
using ReapSeek.Api.Health;
using ReapSeek.Api.Identity;

namespace ReapSeek.Api.Infrastructure;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(RegisterRequest))]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(PasswordChangeRequest))]
[JsonSerializable(typeof(MeResponse))]
public sealed partial class AppJsonContext : JsonSerializerContext;
