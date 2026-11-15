using System.Text.Json.Serialization;

namespace ReapSeek.Identity.Contracts;

public sealed record UserRegisteredV1(long UserId, string Email, string Name)
{
    public const string EventName = "identity.user_registered.v1";
}

public sealed record AccountEmailV1(long UserId, string Kind)
{
    public const string EventName = "identity.account_email.v1";
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(UserRegisteredV1))]
[JsonSerializable(typeof(AccountEmailV1))]
public sealed partial class IdentityEventsJsonContext : JsonSerializerContext;
