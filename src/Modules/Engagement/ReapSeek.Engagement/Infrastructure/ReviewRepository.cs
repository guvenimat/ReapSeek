using Dapper;
using Npgsql;
using ReapSeek.BuildingBlocks.Messaging;
using ReapSeek.Engagement.Contracts;
using ReapSeek.Modules.Catalog;
using ReapSeek.Modules.Identity;
using ReapSeek.Modules.Ordering;
using ReapSeek.Modules.Sellers;
using ReapSeek.Shared.Outbox;
using ReapSeek.Engagement.Api;
using ReapSeek.Engagement.Application;

namespace ReapSeek.Engagement.Infrastructure;

internal sealed class ReviewRepository(NpgsqlDataSource db)
{
    static readonly ModuleOutbox Outbox = new(MessagingModules.Engagement);

    internal sealed record ReviewRecord(long Id, int Rating, string Title, string Body, string AuthorName, string? VariantLabel, DateTime CreatedAt);
    internal sealed record ReviewRowRecord(long Id, int Rating, string Title, string Body, long CustomerId, string? VariantLabel, DateTime CreatedAt);
    internal sealed record CountRecord(int Rating, int Count);
    internal sealed record QuestionRecord(long Id, string Body, long CustomerId, DateTime CreatedAt);
    internal sealed record AnswerRecord(long Id, long QuestionId, long SellerId, string Body, DateTime CreatedAt);
    internal sealed record ReviewUpsertRecord(long ProductId, long CustomerId, int Rating, string Title, string Body, string? VariantLabel);
    internal sealed record SellerQuestionRecord(long Id, long ProductId, string Body, long CustomerId, DateTime CreatedAt, string? MyAnswer, int AnswerCount);
    internal sealed record ModerationReviewRecord(long Id, long ProductId, int Rating, string Title, string Body, long CustomerId, string Status, DateTime CreatedAt);
    internal sealed record ModerationQuestionRecord(long Id, long ProductId, string Body, long CustomerId, string Status, int AnswerCount, DateTime CreatedAt);

    public async Task<bool> ProductExistsAsync(long productId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await CatalogLookups.IsActiveAsync(conn, productId);
    }

    public async Task<ReviewsResponse> ReviewsAsync(long productId, string sort, int page, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var counts = (await conn.QueryAsync<CountRecord>(
            "SELECT rating, count(*)::int AS count FROM reviews WHERE product_id = @ProductId AND status = 'visible' GROUP BY rating",
            new { ProductId = productId })).AsList();
        var total = counts.Sum(c => c.Count);
        var average = total == 0 ? (double?)null : Math.Round(counts.Sum(c => c.Rating * c.Count) / (double)total, 1);
        var order = sort switch { "highest" => "r.rating DESC, r.created_at DESC", "lowest" => "r.rating, r.created_at DESC", _ => "r.created_at DESC" };
        var rows = await WithAuthorsAsync(conn, await conn.QueryAsync<ReviewRowRecord>($"""
            SELECT r.id, r.rating, r.title, r.body, r.customer_id, r.variant_label, r.created_at
            FROM reviews r
            WHERE r.product_id = @ProductId AND r.status = 'visible'
            ORDER BY {order}, r.id DESC LIMIT @Limit OFFSET @Offset
            """, new { ProductId = productId, Limit = ReviewRules.PageSize, Offset = (page - 1) * ReviewRules.PageSize }));
        return new ReviewsResponse(new ReviewSummary(average, total, ReviewRules.Distribution(counts.Select(c => (c.Rating, c.Count)))),
            rows.Select(ToView).ToList(), total, page, ReviewRules.PageSize);
    }

    public async Task<string?> PurchasedVariantAsync(long productId, long customerId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await OrderLookups.DeliveredVariantAsync(conn, productId, customerId);
    }

    public async Task<ReviewView?> MyReviewAsync(long productId, long customerId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<ReviewRowRecord>("""
            SELECT r.id, r.rating, r.title, r.body, r.customer_id, r.variant_label, r.created_at
            FROM reviews r WHERE r.product_id = @ProductId AND r.customer_id = @CustomerId
            """, new { ProductId = productId, CustomerId = customerId });
        return row is null ? null : ToView((await WithAuthorsAsync(conn, [row]))[0]);
    }

    public async Task UpsertReviewAsync(ReviewUpsertRecord review, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await conn.ExecuteAsync("""
            INSERT INTO reviews (product_id, customer_id, rating, title, body, variant_label)
            VALUES (@ProductId, @CustomerId, @Rating, @Title, @Body, @VariantLabel)
            ON CONFLICT (product_id, customer_id) DO UPDATE SET rating = excluded.rating, title = excluded.title, body = excluded.body, updated_at = now()
            """, review, tx);
        await OutboxWriter.EnqueueAsync(conn, tx, OutboxTypes.ProductsChanged, new ProductsChanged([review.ProductId]), OutboxJsonContext.Default.ProductsChanged);
        await tx.CommitAsync(ct);
    }

    public async Task<QuestionsResponse> QuestionsAsync(long productId, int page, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var total = await conn.ExecuteScalarAsync<long>("SELECT count(*) FROM questions WHERE product_id = @ProductId AND status = 'visible'", new { ProductId = productId });
        var questions = (await conn.QueryAsync<QuestionRecord>("""
            SELECT q.id, q.body, q.customer_id, q.created_at FROM questions q
            WHERE q.product_id = @ProductId AND q.status = 'visible' ORDER BY q.created_at DESC, q.id DESC LIMIT @Limit OFFSET @Offset
            """, new { ProductId = productId, Limit = ReviewRules.PageSize, Offset = (page - 1) * ReviewRules.PageSize })).AsList();
        var answers = (await conn.QueryAsync<AnswerRecord>("""
            SELECT a.id, a.question_id, a.seller_id, a.body, a.created_at FROM answers a
            WHERE a.question_id = ANY(@Ids) ORDER BY a.created_at
            """, new { Ids = questions.Select(q => q.Id).ToArray() })).ToLookup(a => a.QuestionId);
        var people = await People.ManyAsync(conn, questions.Select(q => q.CustomerId));
        var sellers = await SellerDirectory.ManyAsync(conn, answers.SelectMany(g => g).Select(a => a.SellerId));
        return new QuestionsResponse(questions.Select(q => new QuestionView(q.Id, q.Body, ReviewRules.Author(NameOf(people, q.CustomerId)), q.CreatedAt,
            answers[q.Id].Select(a => new AnswerView(a.Id, sellers.GetValueOrDefault(a.SellerId)?.Name ?? "", a.Body, a.CreatedAt)).ToList())).ToList(), total, page, ReviewRules.PageSize);
    }

    public async Task<long> AskAsync(long productId, long customerId, string body, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var id = await conn.ExecuteScalarAsync<long>("INSERT INTO questions (product_id, customer_id, body) VALUES (@ProductId, @CustomerId, @Body) RETURNING id",
            new { ProductId = productId, CustomerId = customerId, Body = body }, tx);
        await Outbox.PublishAsync(conn, tx, QuestionAskedV1.EventName, new QuestionAskedV1(id, productId, customerId, DateTime.UtcNow),
            EngagementEventsJsonContext.Default.QuestionAskedV1);
        await tx.CommitAsync(ct);
        return id;
    }


    public async Task<List<SellerQuestionRow>> SellerQuestionsAsync(long sellerId, bool? answered, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var products = (await SellerSnapshots.ProductIdsOfSellerAsync(conn, null, sellerId)).ToArray();
        var rows = (await conn.QueryAsync<SellerQuestionRecord>($"""
            SELECT q.id, q.product_id, q.body, q.customer_id, q.created_at,
                (SELECT a.body FROM answers a WHERE a.question_id = q.id AND a.seller_id = @SellerId) AS my_answer,
                (SELECT count(*)::int FROM answers a WHERE a.question_id = q.id) AS answer_count
            FROM questions q
            WHERE q.status = 'visible' AND q.product_id = ANY(@Products)
              AND (@Answered::boolean IS NULL OR EXISTS (SELECT 1 FROM answers a WHERE a.question_id = q.id AND a.seller_id = @SellerId) = @Answered)
            ORDER BY q.created_at DESC LIMIT 100
            """, new { SellerId = sellerId, Answered = answered, Products = products })).AsList();
        var people = await People.ManyAsync(conn, rows.Select(r => r.CustomerId));
        var info = await CatalogLookups.ProductsAsync(conn, rows.Select(r => r.ProductId));
        return rows.Select(r => new SellerQuestionRow(r.Id, r.ProductId, info.GetValueOrDefault(r.ProductId)?.Title ?? "", info.GetValueOrDefault(r.ProductId)?.Slug ?? "",
            r.Body, ReviewRules.Author(NameOf(people, r.CustomerId)), r.CreatedAt, r.MyAnswer, r.AnswerCount)).ToList();
    }

    public async Task<bool> AnswerAsync(long questionId, long sellerId, string body, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var products = (await SellerSnapshots.ProductIdsOfSellerAsync(conn, tx, sellerId)).ToArray();
        var id = await conn.ExecuteScalarAsync<long?>($"""
            INSERT INTO answers (question_id, seller_id, body)
            SELECT q.id, @SellerId, @Body FROM questions q WHERE q.id = @QuestionId AND q.status = 'visible' AND q.product_id = ANY(@Products)
            ON CONFLICT (question_id, seller_id) DO UPDATE SET body = excluded.body, updated_at = now()
            RETURNING id
            """, new { QuestionId = questionId, SellerId = sellerId, Body = body, Products = products }, tx);
        if (id is null) return false;
        await OutboxWriter.EnqueueAsync(conn, tx, OutboxTypes.QuestionAnswered, new QuestionEventMessage(questionId, id), OutboxJsonContext.Default.QuestionEventMessage);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<ModerationResponse> ModerationAsync(string? status, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var reviews = await conn.QueryAsync<ModerationReviewRecord>("""
            SELECT r.id, r.product_id, r.rating, r.title, r.body, r.customer_id, r.status, r.created_at
            FROM reviews r
            WHERE @Status::text IS NULL OR r.status = @Status ORDER BY r.created_at DESC LIMIT 100
            """, new { Status = status });
        var questions = await conn.QueryAsync<ModerationQuestionRecord>("""
            SELECT q.id, q.product_id, q.body, q.customer_id, q.status,
                (SELECT count(*)::int FROM answers a WHERE a.question_id = q.id) AS answer_count, q.created_at
            FROM questions q
            WHERE @Status::text IS NULL OR q.status = @Status ORDER BY q.created_at DESC LIMIT 100
            """, new { Status = status });
        var reviewList = reviews.AsList();
        var questionList = questions.AsList();
        var people = await People.ManyAsync(conn, reviewList.Select(r => r.CustomerId).Concat(questionList.Select(q => q.CustomerId)));
        var info = await CatalogLookups.ProductsAsync(conn, reviewList.Select(r => r.ProductId).Concat(questionList.Select(q => q.ProductId)));
        string Title(long id) => info.GetValueOrDefault(id)?.Title ?? "";
        return new ModerationResponse(
            reviewList.Select(r => new ModerationReviewRow(r.Id, r.ProductId, Title(r.ProductId), r.Rating, r.Title, r.Body, NameOf(people, r.CustomerId), r.Status, r.CreatedAt)).ToList(),
            questionList.Select(q => new ModerationQuestionRow(q.Id, q.ProductId, Title(q.ProductId), q.Body, NameOf(people, q.CustomerId), q.Status, q.AnswerCount, q.CreatedAt)).ToList());
    }

    public async Task<bool> SetReviewStatusAsync(long id, string status, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var productId = await conn.ExecuteScalarAsync<long?>("UPDATE reviews SET status = @Status WHERE id = @Id RETURNING product_id", new { Id = id, Status = status }, tx);
        if (productId is null) return false;
        await OutboxWriter.EnqueueAsync(conn, tx, OutboxTypes.ProductsChanged, new ProductsChanged([productId.Value]), OutboxJsonContext.Default.ProductsChanged);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<bool> SetQuestionStatusAsync(long id, string status, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.ExecuteAsync("UPDATE questions SET status = @Status WHERE id = @Id", new { Id = id, Status = status }) == 1;
    }

    static string NameOf(Dictionary<long, Person> people, long id) => people.GetValueOrDefault(id)?.Name ?? "";

    static async Task<List<ReviewRecord>> WithAuthorsAsync(NpgsqlConnection conn, IEnumerable<ReviewRowRecord> rows)
    {
        var list = rows.AsList();
        var people = await People.ManyAsync(conn, list.Select(r => r.CustomerId));
        return list.Select(r => new ReviewRecord(r.Id, r.Rating, r.Title, r.Body, NameOf(people, r.CustomerId), r.VariantLabel, r.CreatedAt)).ToList();
    }

    static ReviewView ToView(ReviewRecord r) =>
        new(r.Id, r.Rating, r.Title, r.Body, ReviewRules.Author(r.AuthorName), r.VariantLabel, true, r.CreatedAt);
}
