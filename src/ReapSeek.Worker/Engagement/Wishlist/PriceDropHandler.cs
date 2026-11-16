using ReapSeek.Modules.Identity;
using System.Globalization;
using System.Net;
using Dapper;
using Npgsql;
using ReapSeek.Modules.Catalog;
using ReapSeek.Shared.Analytics;
using ReapSeek.BuildingBlocks.Messaging;
using ReapSeek.Engagement.Contracts;
using ReapSeek.Worker.Notifications;

using ReapSeek.Worker.Ordering;
using ReapSeek.Worker.Marketing;
using ReapSeek.Worker.Engagement;
using ReapSeek.Worker.Identity;
using ReapSeek.Worker.Platform.Analytics;
namespace ReapSeek.Worker.Engagement.Wishlist;

public sealed class PriceDropHandler(NpgsqlDataSource db, EmailSender email, ReapDeepIngestClient reapDeep, IConfiguration cfg, TimeProvider clock)
    : IInboxHandler<PriceDroppedV1>
{
    public const string NotifyHandler = "price-drop-customer-email";

    public sealed record IdRecord(long Id);
    public sealed record PersonRecord(string Email, string Name);
    public sealed record ProductRecord(string Title, string Slug);

    readonly string _storefrontUrl = (cfg["Storefront:PublicUrl"] ?? "http://localhost:3100").TrimEnd('/');

    public async Task HandleAsync(PriceDroppedV1 payload, InboxMessage envelope, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var customer = (await People.ReachableCustomersAsync(conn, [payload.CustomerId])).Count == 1 ? await People.FindAsync(conn, payload.CustomerId) : null;
        var product = await CatalogLookups.ProductAsync(conn, payload.ProductId);
        if (customer is null || product is null) return;

        static string M(decimal v) => v.ToString("N2", CultureInfo.InvariantCulture) + " TRY";
        var url = $"{_storefrontUrl}/p/{product.Slug}";
        var saved = payload.OldPrice - payload.NewPrice;
        var subject = $"Price drop: {product.Title}";
        var text = $"Hi {customer.Name},\n\n{product.Title} from your wishlist is now {M(payload.NewPrice)} (was {M(payload.OldPrice)}, you save {M(saved)}).\n\n{url}\n\nManage your wishlist: {_storefrontUrl}/wishlist\n";
        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:560px;margin:0 auto;color:#1f2330">
              <h1 style="color:#5b2bd6;font-size:22px">A price dropped on your wishlist</h1>
              <p>Hi {WebUtility.HtmlEncode(customer.Name)},</p>
              <p><a href="{WebUtility.HtmlEncode(url)}">{WebUtility.HtmlEncode(product.Title)}</a> is now <strong>{M(payload.NewPrice)}</strong>
                <span style="color:#6b7080;text-decoration:line-through">{M(payload.OldPrice)}</span>. You save {M(saved)}.</p>
              <p><a href="{WebUtility.HtmlEncode(url)}" style="display:inline-block;background:#5b2bd6;color:#fff;padding:10px 18px;border-radius:6px;text-decoration:none">See the product</a></p>
              <p style="color:#6b7080;font-size:12px">You get this because the product is on your wishlist. <a href="{WebUtility.HtmlEncode($"{_storefrontUrl}/wishlist")}">Manage your wishlist</a></p>
            </div>
            """;
        await email.SendAsync(new EmailMessage(customer.Email, subject, text, html), ct);
        if (reapDeep.IsConfigured)
            await reapDeep.CaptureAsync([new CaptureEvent("price_drop_notified", ReapDeepIdentity.DistinctId(payload.CustomerId), $"price-drop-{envelope.Id}",
                clock.GetUtcNow().UtcDateTime, new PurchaseProperties(payload.ProductId.ToString(CultureInfo.InvariantCulture), "", payload.NewPrice, 0, saved, "TRY", 0, "", "server"))], ct);
    }
}
