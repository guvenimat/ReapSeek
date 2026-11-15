using System.Text.Json.Serialization;

namespace ReapSeek.Engagement.Contracts;

public sealed record QuestionAskedV1(long QuestionId, long ProductId, long CustomerId, DateTime AskedAt)
{
    public const string EventName = "engagement.question_asked.v1";
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(QuestionAskedV1))]
public sealed partial class EngagementEventsJsonContext : JsonSerializerContext;
