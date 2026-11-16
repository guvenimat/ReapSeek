using ReapSeek.Modules.Identity;
using System.Globalization;
using System.Net;
using Dapper;
using Npgsql;
using ReapSeek.BuildingBlocks.Messaging;
using ReapSeek.Marketing.Contracts;
using ReapSeek.Worker.Notifications;

namespace ReapSeek.Worker.Marketing;

public sealed class CouponMailHandler(NpgsqlDataSource db, EmailSender email, IConfiguration cfg) : IInboxHandler<CouponIssuedV1>
{
    public const string CouponHandler = "coupon-issued-customer-email";

    public sealed record CouponMailRecord(string Code, string Kind, decimal Value, decimal MinSubtotal, decimal? MaxDiscount, DateTime ExpiresAt,
        string Subject, string Message, long CustomerId);

    readonly string _storefrontUrl = (cfg["Storefront:PublicUrl"] ?? "http://localhost:3100").TrimEnd('/');

    public Task HandleAsync(CouponIssuedV1 message, InboxMessage envelope, CancellationToken ct) => MailAsync(message.CouponId, ct);

    async Task MailAsync(long couponId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var c = await conn.QuerySingleOrDefaultAsync<CouponMailRecord>("""
            SELECT k.code, k.kind, k.value, k.min_subtotal, k.max_discount, k.expires_at, c.subject, c.message, k.customer_id
            FROM coupons k JOIN campaigns c ON c.id = k.campaign_id WHERE k.id = @Id
            """, new { Id = couponId });
        if (c is null || await People.FindAsync(conn, c.CustomerId) is not { } person) return;
        static string M(decimal v) => v.ToString("N2", CultureInfo.InvariantCulture);
        var offer = c.Kind == "percent" ? $"{c.Value:0.##}% off" : $"{M(c.Value)} TRY off";
        var terms = (c.MinSubtotal > 0 ? $"on orders of {M(c.MinSubtotal)} TRY or more" : "on your next order")
            + (c.MaxDiscount is { } cap ? $", up to {M(cap)} TRY" : "") + $". Valid until {c.ExpiresAt.AddDays(-1):d MMMM yyyy}.";
        var text = $"Hi {person.Name},\n\n{c.Message}\n\nYour personal code: {c.Code}\n{offer} {terms}\nIt works once and only with your account.\n\nShop now: {_storefrontUrl}\n";
        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:560px;margin:0 auto;color:#1f2330">
              <h1 style="color:#5b2bd6;font-size:22px">{WebUtility.HtmlEncode(c.Subject)}</h1>
              <p>Hi {WebUtility.HtmlEncode(person.Name)}, {WebUtility.HtmlEncode(c.Message)}</p>
              <p style="font-size:26px;font-weight:700;letter-spacing:2px;background:#efe9fd;color:#5b2bd6;padding:14px;text-align:center;border-radius:8px">{WebUtility.HtmlEncode(c.Code)}</p>
              <p><strong>{WebUtility.HtmlEncode(offer)}</strong> {WebUtility.HtmlEncode(terms)} It works once and only with your account.</p>
              <p><a href="{WebUtility.HtmlEncode(_storefrontUrl)}" style="display:inline-block;background:#5b2bd6;color:#fff;padding:10px 18px;border-radius:6px;text-decoration:none">Shop now</a></p>
            </div>
            """;
        await email.SendAsync(new EmailMessage(person.Email, c.Subject, text, html), ct);
    }
}
