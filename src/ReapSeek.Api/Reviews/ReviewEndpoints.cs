using ReapSeek.Api.Identity;
using ReapSeek.Api.Infrastructure;
using ReapSeek.Shared;

namespace ReapSeek.Api.Reviews;

public static class ReviewEndpoints
{
    public static void MapReviews(this RouteGroupBuilder api)
    {
        var products = api.MapGroup("/catalog/products/{id:long}");
        products.MapGet("/reviews", async (long id, ReviewRepository reviews, string? sort, int? page, CancellationToken ct) =>
        {
            var s = sort is { Length: > 0 } ? Ensure.OneOf(sort, "sort", ReviewRules.Sorts) : "newest";
            return ApiResults.Ok(await reviews.ReviewsAsync(id, s, Ensure.InRange(page ?? 1, 1, 1000, "page"), ct), AppJsonContext.Default.ReviewsResponse);
        });
        products.MapGet("/questions", async (long id, ReviewRepository reviews, int? page, CancellationToken ct) =>
            ApiResults.Ok(await reviews.QuestionsAsync(id, Ensure.InRange(page ?? 1, 1, 1000, "page"), ct), AppJsonContext.Default.QuestionsResponse));

        products.MapGet("/my-review", async (long id, UserInfo user, ReviewRepository reviews, CancellationToken ct) =>
            ApiResults.Ok(new MyReviewResponse(await reviews.PurchasedVariantAsync(id, user.Id, ct) is not null, await reviews.MyReviewAsync(id, user.Id, ct)),
                AppJsonContext.Default.MyReviewResponse)).RequireCustomer();
        products.MapPut("/review", async (long id, ProductReviewRequest req, UserInfo user, ReviewRepository reviews, CancellationToken ct) =>
        {
            var review = ReviewRules.Review(req);
            if (!await reviews.ProductExistsAsync(id, ct)) throw new NotFoundException("Product not found");
            var variant = await reviews.PurchasedVariantAsync(id, user.Id, ct);
            if (variant is null) return ApiResults.Forbidden("Only customers who received this product can review it");
            await reviews.UpsertReviewAsync(new ReviewRepository.ReviewUpsertRecord(id, user.Id, review.Rating, review.Title, review.Body, variant), ct);
            return ApiResults.Ok(new MyReviewResponse(true, await reviews.MyReviewAsync(id, user.Id, ct)), AppJsonContext.Default.MyReviewResponse);
        }).RequireCustomer().RequireRateLimiting(HostingExtensions.ContentRateLimit);
        products.MapPost("/questions", async (long id, AskRequest req, UserInfo user, ReviewRepository reviews, CancellationToken ct) =>
        {
            var body = ReviewRules.Question(req);
            if (!await reviews.ProductExistsAsync(id, ct)) throw new NotFoundException("Product not found");
            await reviews.AskAsync(id, user.Id, body, ct);
            return ApiResults.Created(await reviews.QuestionsAsync(id, 1, ct), AppJsonContext.Default.QuestionsResponse);
        }).RequireCustomer().RequireRateLimiting(HostingExtensions.ContentRateLimit);

        var seller = api.MapGroup("/seller/questions").RequireSeller();
        seller.MapGet("/", async (UserInfo user, ReviewRepository reviews, bool? answered, CancellationToken ct) =>
            ApiResults.Ok(new SellerQuestionsResponse(await reviews.SellerQuestionsAsync(SellerOf(user), answered, ct)), AppJsonContext.Default.SellerQuestionsResponse));
        seller.MapPut("/{id:long}/answer", async (long id, AnswerRequest req, UserInfo user, ReviewRepository reviews, CancellationToken ct) =>
        {
            var body = ReviewRules.Answer(req);
            if (!await reviews.AnswerAsync(id, SellerOf(user), body, ct)) throw new NotFoundException("Question not found");
            return ApiResults.Ok(new SellerQuestionsResponse((await reviews.SellerQuestionsAsync(SellerOf(user), null, ct)).Where(q => q.Id == id).ToList()),
                AppJsonContext.Default.SellerQuestionsResponse);
        }).RequireRateLimiting(HostingExtensions.ContentRateLimit);

        var admin = api.MapGroup("/backoffice").RequireAdmin();
        admin.MapGet("/moderation", async (ReviewRepository reviews, string? status, CancellationToken ct) =>
            ApiResults.Ok(await reviews.ModerationAsync(status is { Length: > 0 } ? Ensure.OneOf(status, "status", ReviewRules.Statuses) : null, ct),
                AppJsonContext.Default.ModerationResponse));
        admin.MapPatch("/reviews/{id:long}", async (long id, ModerationRequest req, ReviewRepository reviews, CancellationToken ct) =>
            await reviews.SetReviewStatusAsync(id, Ensure.OneOf(req.Status ?? "", "status", ReviewRules.Statuses), ct) ? Results.NoContent() : ApiResults.NotFound("Review not found"));
        admin.MapPatch("/questions/{id:long}", async (long id, ModerationRequest req, ReviewRepository reviews, CancellationToken ct) =>
            await reviews.SetQuestionStatusAsync(id, Ensure.OneOf(req.Status ?? "", "status", ReviewRules.Statuses), ct) ? Results.NoContent() : ApiResults.NotFound("Question not found"));
    }

    static long SellerOf(UserInfo user) => user.SellerId ?? throw new NotFoundException("Seller not found");
}
