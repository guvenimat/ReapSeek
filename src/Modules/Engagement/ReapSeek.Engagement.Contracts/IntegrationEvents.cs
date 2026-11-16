using System.Text.Json.Serialization;

namespace ReapSeek.Engagement.Contracts;

public sealed record QuestionAskedV1(long QuestionId, long ProductId, long CustomerId, DateTime AskedAt)
{
    public const string EventName = "engagement.question_asked.v1";
}

public sealed record QuestionAnsweredV1(long QuestionId, long AnswerId)
{
    public const string EventName = "engagement.question_answered.v1";
}

public sealed record PriceDroppedV1(long CustomerId, long ProductId, decimal OldPrice, decimal NewPrice)
{
    public const string EventName = "engagement.price_dropped.v1";
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(QuestionAskedV1))]
[JsonSerializable(typeof(QuestionAnsweredV1))]
[JsonSerializable(typeof(PriceDroppedV1))]
public sealed partial class EngagementEventsJsonContext : JsonSerializerContext;
