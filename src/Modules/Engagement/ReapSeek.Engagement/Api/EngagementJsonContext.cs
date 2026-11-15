using System.Text.Json.Serialization;

namespace ReapSeek.Engagement.Api;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(ReviewsResponse))]
[JsonSerializable(typeof(MyReviewResponse))]
[JsonSerializable(typeof(ProductReviewRequest))]
[JsonSerializable(typeof(QuestionsResponse))]
[JsonSerializable(typeof(AskRequest))]
[JsonSerializable(typeof(AnswerRequest))]
[JsonSerializable(typeof(SellerQuestionsResponse))]
[JsonSerializable(typeof(ModerationResponse))]
[JsonSerializable(typeof(ModerationRequest))]
[JsonSerializable(typeof(WishlistResponse))]
[JsonSerializable(typeof(WishlistIdsResponse))]
[JsonSerializable(typeof(WishlistRequest))]
internal sealed partial class EngagementJsonContext : JsonSerializerContext;
