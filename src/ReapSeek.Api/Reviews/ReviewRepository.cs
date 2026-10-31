using Dapper;
using Npgsql;
using ReapSeek.Shared.Outbox;

namespace ReapSeek.Api.Reviews;

public sealed class ReviewRepository(NpgsqlDataSource db)
{
    public sealed record ReviewRecord(long Id, int Rating, string Title, string Body, string AuthorName, string? VariantLabel, DateTime CreatedAt);
    public sealed record CountRecord(int Rating, int Count);
    public sealed record QuestionRecord(long Id, string Body, string AuthorName, DateTime CreatedAt);
    public sealed record AnswerRecord(long Id, long QuestionId, string SellerName, string Body, DateTime CreatedAt);
    public sealed record ReviewUpsertRecord(long ProductId, long CustomerId, int Rating, string Title, string Body, string? VariantLabel);
    public sealed record SellerQuestionRecord(long Id, long ProductId, string ProductTitle, string ProductSlug, string Body, string AuthorName, DateTime CreatedAt, string? MyAnswer, int AnswerCount);
    public sealed record ModerationReviewRecord(long Id, long ProductId, string ProductTitle, int Rating, string Title, string Body, string AuthorName, string Status, DateTime CreatedAt);
    public sealed record ModerationQuestionRecord(long Id, long ProductId, string ProductTitle, string Body, string AuthorName, string Status, int AnswerCount, DateTime CreatedAt);

    public async Task<bool> ProductExistsAsync(long productId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM products WHERE id = @Id AND status = 'active')", new { Id = productId });
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
        var rows = await conn.QueryAsync<ReviewRecord>($"""
            SELECT r.id, r.rating, r.title, r.body, u.name AS author_name, r.variant_label, r.created_at
            FROM reviews r JOIN users u ON u.id = r.customer_id
            WHERE r.product_id = @ProductId AND r.status = 'visible'
            ORDER BY {order}, r.id DESC LIMIT @Limit OFFSET @Offset
            """, new { ProductId = productId, Limit = ReviewRules.PageSize, Offset = (page - 1) * ReviewRules.PageSize });
        return new ReviewsResponse(new ReviewSummary(average, total, ReviewRules.Distribution(counts.Select(c => (c.Rating, c.Count)))),
            rows.Select(ToView).ToList(), total, page, ReviewRules.PageSize);
    }

    public async Task<string?> PurchasedVariantAsync(long productId, long customerId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<string?>("""
            SELECT l.variant_label FROM order_lines l JOIN order_packages p ON p.id = l.package_id JOIN orders o ON o.id = l.order_id
            WHERE l.product_id = @ProductId AND o.customer_id = @CustomerId AND p.status = 'delivered'
            ORDER BY p.delivered_at DESC LIMIT 1
            """, new { ProductId = productId, CustomerId = customerId });
    }

    public async Task<ReviewView?> MyReviewAsync(long productId, long customerId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<ReviewRecord>("""
            SELECT r.id, r.rating, r.title, r.body, u.name AS author_name, r.variant_label, r.created_at
            FROM reviews r JOIN users u ON u.id = r.customer_id WHERE r.product_id = @ProductId AND r.customer_id = @CustomerId
            """, new { ProductId = productId, CustomerId = customerId });
        return row is null ? null : ToView(row);
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
            SELECT q.id, q.body, u.name AS author_name, q.created_at FROM questions q JOIN users u ON u.id = q.customer_id
            WHERE q.product_id = @ProductId AND q.status = 'visible' ORDER BY q.created_at DESC, q.id DESC LIMIT @Limit OFFSET @Offset
            """, new { ProductId = productId, Limit = ReviewRules.PageSize, Offset = (page - 1) * ReviewRules.PageSize })).AsList();
        var answers = (await conn.QueryAsync<AnswerRecord>("""
            SELECT a.id, a.question_id, s.name AS seller_name, a.body, a.created_at FROM answers a JOIN sellers s ON s.id = a.seller_id
            WHERE a.question_id = ANY(@Ids) ORDER BY a.created_at
            """, new { Ids = questions.Select(q => q.Id).ToArray() })).ToLookup(a => a.QuestionId);
        return new QuestionsResponse(questions.Select(q => new QuestionView(q.Id, q.Body, ReviewRules.Author(q.AuthorName), q.CreatedAt,
            answers[q.Id].Select(a => new AnswerView(a.Id, a.SellerName, a.Body, a.CreatedAt)).ToList())).ToList(), total, page, ReviewRules.PageSize);
    }

    public async Task<long> AskAsync(long productId, long customerId, string body, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var id = await conn.ExecuteScalarAsync<long>("INSERT INTO questions (product_id, customer_id, body) VALUES (@ProductId, @CustomerId, @Body) RETURNING id",
            new { ProductId = productId, CustomerId = customerId, Body = body }, tx);
        await OutboxWriter.EnqueueAsync(conn, tx, OutboxTypes.QuestionAsked, new QuestionEventMessage(id, null), OutboxJsonContext.Default.QuestionEventMessage);
        await tx.CommitAsync(ct);
        return id;
    }

    const string SellerProducts = "SELECT DISTINCT v.product_id FROM offers o JOIN product_variants v ON v.id = o.variant_id WHERE o.seller_id = @SellerId";

    public async Task<List<SellerQuestionRow>> SellerQuestionsAsync(long sellerId, bool? answered, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<SellerQuestionRecord>($"""
            SELECT q.id, q.product_id, p.title AS product_title, p.slug AS product_slug, q.body, u.name AS author_name, q.created_at,
                (SELECT a.body FROM answers a WHERE a.question_id = q.id AND a.seller_id = @SellerId) AS my_answer,
                (SELECT count(*)::int FROM answers a WHERE a.question_id = q.id) AS answer_count
            FROM questions q JOIN products p ON p.id = q.product_id JOIN users u ON u.id = q.customer_id
            WHERE q.status = 'visible' AND q.product_id IN ({SellerProducts})
              AND (@Answered::boolean IS NULL OR EXISTS (SELECT 1 FROM answers a WHERE a.question_id = q.id AND a.seller_id = @SellerId) = @Answered)
            ORDER BY q.created_at DESC LIMIT 100
            """, new { SellerId = sellerId, Answered = answered });
        return rows.Select(r => new SellerQuestionRow(r.Id, r.ProductId, r.ProductTitle, r.ProductSlug, r.Body, ReviewRules.Author(r.AuthorName), r.CreatedAt,
            r.MyAnswer, r.AnswerCount)).ToList();
    }

    public async Task<bool> AnswerAsync(long questionId, long sellerId, string body, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var id = await conn.ExecuteScalarAsync<long?>($"""
            INSERT INTO answers (question_id, seller_id, body)
            SELECT q.id, @SellerId, @Body FROM questions q WHERE q.id = @QuestionId AND q.status = 'visible' AND q.product_id IN ({SellerProducts})
            ON CONFLICT (question_id, seller_id) DO UPDATE SET body = excluded.body, updated_at = now()
            RETURNING id
            """, new { QuestionId = questionId, SellerId = sellerId, Body = body }, tx);
        if (id is null) return false;
        await OutboxWriter.EnqueueAsync(conn, tx, OutboxTypes.QuestionAnswered, new QuestionEventMessage(questionId, id), OutboxJsonContext.Default.QuestionEventMessage);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<ModerationResponse> ModerationAsync(string? status, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var reviews = await conn.QueryAsync<ModerationReviewRecord>("""
            SELECT r.id, r.product_id, p.title AS product_title, r.rating, r.title, r.body, u.name AS author_name, r.status, r.created_at
            FROM reviews r JOIN products p ON p.id = r.product_id JOIN users u ON u.id = r.customer_id
            WHERE @Status::text IS NULL OR r.status = @Status ORDER BY r.created_at DESC LIMIT 100
            """, new { Status = status });
        var questions = await conn.QueryAsync<ModerationQuestionRecord>("""
            SELECT q.id, q.product_id, p.title AS product_title, q.body, u.name AS author_name, q.status,
                (SELECT count(*)::int FROM answers a WHERE a.question_id = q.id) AS answer_count, q.created_at
            FROM questions q JOIN products p ON p.id = q.product_id JOIN users u ON u.id = q.customer_id
            WHERE @Status::text IS NULL OR q.status = @Status ORDER BY q.created_at DESC LIMIT 100
            """, new { Status = status });
        return new ModerationResponse(
            reviews.Select(r => new ModerationReviewRow(r.Id, r.ProductId, r.ProductTitle, r.Rating, r.Title, r.Body, r.AuthorName, r.Status, r.CreatedAt)).ToList(),
            questions.Select(q => new ModerationQuestionRow(q.Id, q.ProductId, q.ProductTitle, q.Body, q.AuthorName, q.Status, q.AnswerCount, q.CreatedAt)).ToList());
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

    static ReviewView ToView(ReviewRecord r) =>
        new(r.Id, r.Rating, r.Title, r.Body, ReviewRules.Author(r.AuthorName), r.VariantLabel, true, r.CreatedAt);
}
