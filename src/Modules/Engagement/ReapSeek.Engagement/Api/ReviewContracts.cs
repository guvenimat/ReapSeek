namespace ReapSeek.Engagement.Api;

internal sealed record ReviewSummary(double? Average, int Count, List<int> Distribution);
internal sealed record ReviewView(long Id, int Rating, string Title, string Body, string Author, string? VariantLabel, bool Verified, DateTime CreatedAt);
internal sealed record ReviewsResponse(ReviewSummary Summary, List<ReviewView> Reviews, long Total, int Page, int PageSize);
internal sealed record MyReviewResponse(bool Eligible, ReviewView? Review);
internal sealed record ProductReviewRequest(int? Rating, string? Title, string? Body);

internal sealed record AnswerView(long Id, string SellerName, string Body, DateTime CreatedAt);
internal sealed record QuestionView(long Id, string Body, string Author, DateTime CreatedAt, List<AnswerView> Answers);
internal sealed record QuestionsResponse(List<QuestionView> Questions, long Total, int Page, int PageSize);
internal sealed record AskRequest(string? Body);
internal sealed record AnswerRequest(string? Body);

internal sealed record SellerQuestionRow(long Id, long ProductId, string ProductTitle, string ProductSlug, string Body, string Author, DateTime CreatedAt, string? MyAnswer, int AnswerCount);
internal sealed record SellerQuestionsResponse(List<SellerQuestionRow> Questions);

internal sealed record ModerationReviewRow(long Id, long ProductId, string ProductTitle, int Rating, string Title, string Body, string Author, string Status, DateTime CreatedAt);
internal sealed record ModerationQuestionRow(long Id, long ProductId, string ProductTitle, string Body, string Author, string Status, int AnswerCount, DateTime CreatedAt);
internal sealed record ModerationResponse(List<ModerationReviewRow> Reviews, List<ModerationQuestionRow> Questions);
internal sealed record ModerationRequest(string? Status);
