using ReapSeek.Engagement.Api;
using ReapSeek.Shared;

namespace ReapSeek.Engagement.Application;

internal sealed record ValidReview(int Rating, string Title, string Body);

internal static class ReviewRules
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
