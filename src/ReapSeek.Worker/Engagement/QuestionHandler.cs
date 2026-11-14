using ReapSeek.Modules.Catalog;
using ReapSeek.Modules.Identity;
using ReapSeek.Modules.Sellers;
using System.Net;
using System.Text.Json;
using Dapper;
using Npgsql;
using ReapSeek.Shared.Outbox;
using ReapSeek.Worker.Notifications;
using ReapSeek.Worker.Outbox;

using ReapSeek.Worker.Ordering;
using ReapSeek.Worker.Marketing;
using ReapSeek.Worker.Identity;
using ReapSeek.Worker.Platform.Analytics;
namespace ReapSeek.Worker.Engagement;

public sealed class QuestionHandler(NpgsqlDataSource db, EmailSender email, IConfiguration cfg)
{
    public sealed record QuestionRecord(string Body, long ProductId, long CustomerId);
    public sealed record AnswerRowRecord(string Body, long SellerId);
    public sealed record RecipientRecord(string Email, string Name);
    public sealed record AnswerRecord(string Body, string SellerName);

    readonly string _storefrontUrl = (cfg["Storefront:PublicUrl"] ?? "http://localhost:3100").TrimEnd('/');
    readonly string _backofficeUrl = (cfg["Backoffice:PublicUrl"] ?? "http://localhost:3101").TrimEnd('/');

    public async Task HandleAsync(OutboxRepository.MessageRecord message, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize(message.Payload, OutboxJsonContext.Default.QuestionEventMessage)
            ?? throw new InvalidOperationException($"Outbox message {message.Id} has an empty payload");
        await using var conn = await db.OpenConnectionAsync(ct);
        var question = await conn.QuerySingleOrDefaultAsync<QuestionRecord>(
            "SELECT body, product_id, customer_id FROM engagement.questions WHERE id = @Id", new { Id = payload.QuestionId });
        if (question is null || await CatalogLookups.ProductAsync(conn, question.ProductId) is not { } product) return;
        var q = new { question.Body, ProductTitle = product.Title, ProductSlug = product.Slug };

        if (message.Type == OutboxTypes.QuestionAsked)
        {
            var sellers = new List<Person>();
            foreach (var sellerId in await CatalogLookups.ActiveSellersOfProductAsync(conn, question.ProductId))
                sellers.AddRange(await People.SellerStaffAsync(conn, sellerId));
            var url = $"{_backofficeUrl}/#/questions";
            foreach (var s in sellers)
                await email.SendAsync(new EmailMessage(s.Email, $"New question about {q.ProductTitle}",
                    $"Hi {s.Name},\n\nA shopper asked about {q.ProductTitle}:\n\n\"{q.Body}\"\n\nAnswer it in the seller center: {url}\n",
                    $"""<div style="font-family:Arial,sans-serif;max-width:560px;margin:0 auto;color:#1f2330"><h1 style="color:#5b2bd6;font-size:22px">New question</h1><p>A shopper asked about <strong>{WebUtility.HtmlEncode(q.ProductTitle)}</strong>:</p><blockquote>{WebUtility.HtmlEncode(q.Body)}</blockquote><p><a href="{WebUtility.HtmlEncode(url)}">Answer it in the seller center</a></p></div>"""), ct);
            return;
        }

        var row = await conn.QuerySingleOrDefaultAsync<AnswerRowRecord>(
            "SELECT body, seller_id FROM engagement.answers WHERE id = @Id", new { Id = payload.AnswerId });
        if (row is null || await SellerDirectory.FindAsync(conn, row.SellerId) is not { } seller) return;
        var answer = new AnswerRecord(row.Body, seller.Name);
        var asker = await People.GetAsync(conn, question.CustomerId);
        var productUrl = $"{_storefrontUrl}/p/{q.ProductSlug}#questions";
        await email.SendAsync(new EmailMessage(asker.Email, $"{answer.SellerName} answered your question",
            $"Hi {asker.Name},\n\nYou asked about {q.ProductTitle}: \"{q.Body}\"\n\n{answer.SellerName} answered: \"{answer.Body}\"\n\n{productUrl}\n",
            $"""<div style="font-family:Arial,sans-serif;max-width:560px;margin:0 auto;color:#1f2330"><h1 style="color:#5b2bd6;font-size:22px">Your question was answered</h1><p>You asked about <strong>{WebUtility.HtmlEncode(q.ProductTitle)}</strong>: {WebUtility.HtmlEncode(q.Body)}</p><p><strong>{WebUtility.HtmlEncode(answer.SellerName)}</strong>: {WebUtility.HtmlEncode(answer.Body)}</p><p><a href="{WebUtility.HtmlEncode(productUrl)}">See the product</a></p></div>"""), ct);
    }
}
