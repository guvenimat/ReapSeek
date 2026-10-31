using ReapSeek.Shared;

namespace ReapSeek.Api.Reviews;

public sealed record ReviewSummary(double? Average, int Count, List<int> Distribution);
public sealed record ReviewView(long Id, int Rating, string Title, string Body, string Author, string? VariantLabel, bool Verified, DateTime CreatedAt);
public sealed record ReviewsResponse(ReviewSummary Summary, List<ReviewView> Reviews, long Total, int Page, int PageSize);
public sealed record MyReviewResponse(bool Eligible, ReviewView? Review);
public sealed record ProductReviewRequest(int? Rating, string? Title, string? Body);

public sealed record AnswerView(long Id, string SellerName, string Body, DateTime CreatedAt);
public sealed record QuestionView(long Id, string Body, string Author, DateTime CreatedAt, List<AnswerView> Answers);
public sealed record QuestionsResponse(List<QuestionView> Questions, long Total, int Page, int PageSize);
public sealed record AskRequest(string? Body);
public sealed record AnswerRequest(string? Body);

public sealed record SellerQuestionRow(long Id, long ProductId, string ProductTitle, string ProductSlug, string Body, string Author, DateTime CreatedAt, string? MyAnswer, int AnswerCount);
public sealed record SellerQuestionsResponse(List<SellerQuestionRow> Questions);

public sealed record ModerationReviewRow(long Id, long ProductId, string ProductTitle, int Rating, string Title, string Body, string Author, string Status, DateTime CreatedAt);
public sealed record ModerationQuestionRow(long Id, long ProductId, string ProductTitle, string Body, string Author, string Status, int AnswerCount, DateTime CreatedAt);
public sealed record ModerationResponse(List<ModerationReviewRow> Reviews, List<ModerationQuestionRow> Questions);
public sealed record ModerationRequest(string? Status);

public sealed record ValidReview(int Rating, string Title, string Body);

public static class ReviewRules
{
    public const int PageSize = 10;
    public static readonly string[] Statuses = ["visible", "hidden"];
    public static readonly string[] Sorts = ["newest", "highest", "lowest"];

    public static ValidReview Review(ProductReviewRequest req)
    {
        Ensure.That(req.Rating is >= 1 and <= 5, "rating must be between 1 and 5");
        var title = Ensure.NotBlank(req.Title, "title", 100);
        Ensure.That(title.Length >= 3, "title must have at least 3 characters");
        var body = Ensure.NotBlank(req.Body, "body", 2000);
        Ensure.That(body.Length >= 10, "Tell other shoppers a little more (at least 10 characters)");
        return new ValidReview(req.Rating!.Value, title, body);
    }

    public static string Question(AskRequest req)
    {
        var body = Ensure.NotBlank(req.Body, "body", 500);
        Ensure.That(body.Length >= 10, "A question needs at least 10 characters");
        return body;
    }

    public static string Answer(AnswerRequest req)
    {
        var body = Ensure.NotBlank(req.Body, "body", 1000);
        Ensure.That(body.Length >= 2, "An answer needs at least 2 characters");
        return body;
    }

    public static string Author(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "Customer",
            1 => parts[0],
            _ => $"{parts[0]} {char.ToUpperInvariant(parts[^1][0])}.",
        };
    }

    public static List<int> Distribution(IEnumerable<(int Rating, int Count)> counts)
    {
        var map = counts.ToDictionary(c => c.Rating, c => c.Count);
        return [.. Enumerable.Range(1, 5).Reverse().Select(r => map.GetValueOrDefault(r))];
    }
}
