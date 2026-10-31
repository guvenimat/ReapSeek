using System.Net;
using System.Text.Json;
using Dapper;
using Npgsql;
using ReapSeek.Shared.Outbox;
using ReapSeek.Worker.Notifications;
using ReapSeek.Worker.Outbox;

namespace ReapSeek.Worker.Orders;

public sealed class QuestionHandler(NpgsqlDataSource db, EmailSender email, IConfiguration cfg)
{
    public sealed record QuestionRecord(string Body, string ProductTitle, string ProductSlug, string AskerEmail, string AskerName);
    public sealed record RecipientRecord(string Email, string Name);
    public sealed record AnswerRecord(string Body, string SellerName);

    readonly string _storefrontUrl = (cfg["Storefront:PublicUrl"] ?? "http://localhost:3100").TrimEnd('/');
    readonly string _backofficeUrl = (cfg["Backoffice:PublicUrl"] ?? "http://localhost:3101").TrimEnd('/');

    public async Task HandleAsync(OutboxRepository.MessageRecord message, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize(message.Payload, OutboxJsonContext.Default.QuestionEventMessage)
            ?? throw new InvalidOperationException($"Outbox message {message.Id} has an empty payload");
        await using var conn = await db.OpenConnectionAsync(ct);
        var q = await conn.QuerySingleOrDefaultAsync<QuestionRecord>("""
            SELECT q.body, p.title AS product_title, p.slug AS product_slug, u.email AS asker_email, u.name AS asker_name
            FROM questions q JOIN products p ON p.id = q.product_id JOIN users u ON u.id = q.customer_id WHERE q.id = @Id
            """, new { Id = payload.QuestionId });
        if (q is null) return;

        if (message.Type == OutboxTypes.QuestionAsked)
        {
            var sellers = await conn.QueryAsync<RecipientRecord>("""
                SELECT DISTINCT u.email, u.name FROM users u
                WHERE u.seller_id IN (SELECT o.seller_id FROM offers o JOIN product_variants v ON v.id = o.variant_id JOIN questions q ON q.product_id = v.product_id
                                      WHERE q.id = @Id AND o.status = 'active')
                """, new { Id = payload.QuestionId });
            var url = $"{_backofficeUrl}/#/questions";
            foreach (var s in sellers)
                await email.SendAsync(new EmailMessage(s.Email, $"New question about {q.ProductTitle}",
                    $"Hi {s.Name},\n\nA shopper asked about {q.ProductTitle}:\n\n\"{q.Body}\"\n\nAnswer it in the seller center: {url}\n",
                    $"""<div style="font-family:Arial,sans-serif;max-width:560px;margin:0 auto;color:#1f2330"><h1 style="color:#5b2bd6;font-size:22px">New question</h1><p>A shopper asked about <strong>{WebUtility.HtmlEncode(q.ProductTitle)}</strong>:</p><blockquote>{WebUtility.HtmlEncode(q.Body)}</blockquote><p><a href="{WebUtility.HtmlEncode(url)}">Answer it in the seller center</a></p></div>"""), ct);
            return;
        }

        var answer = await conn.QuerySingleOrDefaultAsync<AnswerRecord>(
            "SELECT a.body, s.name AS seller_name FROM answers a JOIN sellers s ON s.id = a.seller_id WHERE a.id = @Id", new { Id = payload.AnswerId });
        if (answer is null) return;
        var productUrl = $"{_storefrontUrl}/p/{q.ProductSlug}#questions";
        await email.SendAsync(new EmailMessage(q.AskerEmail, $"{answer.SellerName} answered your question",
            $"Hi {q.AskerName},\n\nYou asked about {q.ProductTitle}: \"{q.Body}\"\n\n{answer.SellerName} answered: \"{answer.Body}\"\n\n{productUrl}\n",
            $"""<div style="font-family:Arial,sans-serif;max-width:560px;margin:0 auto;color:#1f2330"><h1 style="color:#5b2bd6;font-size:22px">Your question was answered</h1><p>You asked about <strong>{WebUtility.HtmlEncode(q.ProductTitle)}</strong>: {WebUtility.HtmlEncode(q.Body)}</p><p><strong>{WebUtility.HtmlEncode(answer.SellerName)}</strong>: {WebUtility.HtmlEncode(answer.Body)}</p><p><a href="{WebUtility.HtmlEncode(productUrl)}">See the product</a></p></div>"""), ct);
    }
}
