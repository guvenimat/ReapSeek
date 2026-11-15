using System.Text.Json.Serialization;

namespace ReapSeek.Marketing.Api;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(ReapDeepAlertPayload))]
[JsonSerializable(typeof(AlertSubscriptionRequest))]
[JsonSerializable(typeof(AlertsResponse))]
[JsonSerializable(typeof(CampaignRequest))]
[JsonSerializable(typeof(CampaignRow))]
[JsonSerializable(typeof(CampaignsResponse))]
[JsonSerializable(typeof(CohortsListResponse))]
[JsonSerializable(typeof(AudienceRequest))]
[JsonSerializable(typeof(AudiencePreviewResponse))]
[JsonSerializable(typeof(CustomerCouponsResponse))]
internal sealed partial class MarketingJsonContext : JsonSerializerContext;
