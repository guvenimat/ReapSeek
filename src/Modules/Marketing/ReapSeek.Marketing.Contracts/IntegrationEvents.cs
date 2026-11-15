using System.Text.Json.Serialization;

namespace ReapSeek.Marketing.Contracts;

public sealed record CampaignSendV1(long CampaignId)
{
    public const string EventName = "marketing.campaign_send.v1";
}

public sealed record CouponIssuedV1(long CampaignId, long CouponId)
{
    public const string EventName = "marketing.coupon_issued.v1";
}

public sealed record AlertReceivedV1(long EventId)
{
    public const string EventName = "marketing.alert_received.v1";
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(CampaignSendV1))]
[JsonSerializable(typeof(CouponIssuedV1))]
[JsonSerializable(typeof(AlertReceivedV1))]
public sealed partial class MarketingEventsJsonContext : JsonSerializerContext;
