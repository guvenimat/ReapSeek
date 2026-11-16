using ReapSeek.Modules.Identity;
using System.Net;
using System.Text.Json;
using Dapper;
using Npgsql;
using ReapSeek.Domain.Ordering;
using ReapSeek.Modules.Ordering;
using ReapSeek.Shared.Outbox;
using ReapSeek.Worker.Notifications;
using ReapSeek.Worker.Outbox;

using ReapSeek.Worker.Marketing;
using ReapSeek.Worker.Engagement;
using ReapSeek.Worker.Identity;
using ReapSeek.Worker.Platform.Analytics;
namespace ReapSeek.Worker.Ordering;

public sealed class PackageReminderHandler(NpgsqlDataSource db, EmailSender email, IConfiguration cfg)
{
    public sealed record PackageRecord(string OrderNumber, string Status, long SellerId, DateTime PaidAt);
    public sealed record IdRecord(Guid Id);
    public sealed record SellerRecord(long SellerId);
    public sealed record PersonRecord(string Email, string Name);

    readonly string _backofficeUrl = (cfg["Backoffice:PublicUrl"] ?? "http://localhost:3101").TrimEnd('/');
    readonly int _cancelDays = (int)Math.Round(cfg.GetValue("Shipping:CancelAfterSeconds", 5 * 24 * 3600) / 86400.0);

    public async Task HandleAsync(OutboxMessageRecord message, CancellationToken ct)
    {
        var m = JsonSerializer.Deserialize(message.Payload, OutboxJsonContext.Default.PackageReminderMessage)
            ?? throw new InvalidOperationException($"Outbox message {message.Id} has an empty payload");
        await using var conn = await db.OpenConnectionAsync(ct);
        var p = await conn.QuerySingleOrDefaultAsync<PackageRecord>("""
            SELECT o.number AS order_number, p.status, p.seller_id, o.paid_at FROM order_packages p JOIN orders o ON o.id = p.order_id WHERE p.id = @Id
            """, new IdRecord(m.PackageId));
        if (p is null || p.Status != "preparing") return;
        var url = $"{_backofficeUrl}/#/packages";
        var deadline = p.PaidAt.AddDays(Math.Max(1, _cancelDays)).ToString("d MMM yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        foreach (var seller in await People.SellerStaffAsync(conn, p.SellerId))
            await email.SendAsync(new EmailMessage(seller.Email, $"Ship order {p.OrderNumber} soon",
                $"Hi {seller.Name},\n\nOrder {p.OrderNumber} was paid on {p.PaidAt:d MMM yyyy} and has not shipped yet. If it does not ship by {deadline} UTC we cancel your package and refund the customer.\n\nYour packages: {url}\n",
                $"""<div style="font-family:Arial,sans-serif;max-width:560px;margin:0 auto;color:#1f2330"><h1 style="color:#ec6416;font-size:20px">Order {WebUtility.HtmlEncode(p.OrderNumber)} is waiting to ship</h1><p>It was paid on {p.PaidAt:d MMM yyyy}. If it does not ship by <strong>{deadline} UTC</strong>, we cancel your package and refund the customer.</p><p><a href="{WebUtility.HtmlEncode(url)}" style="display:inline-block;background:#5b2bd6;color:#fff;padding:10px 18px;border-radius:6px;text-decoration:none">Ship it now</a></p></div>"""), ct);
    }
}
