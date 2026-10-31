using System.Globalization;
using System.Net;
using System.Text.Json;
using Dapper;
using Npgsql;
using ReapSeek.Domain.Marketing;
using ReapSeek.Shared.Analytics;
using ReapSeek.Shared.Marketing;
using ReapSeek.Shared.Outbox;
using ReapSeek.Worker.Notifications;
using ReapSeek.Worker.Outbox;

namespace ReapSeek.Worker.Orders;

public sealed class CampaignHandler(NpgsqlDataSource db, ReapDeepAudience audience, EmailSender email, IConfiguration cfg, TimeProvider clock,
    ILogger<CampaignHandler> log)
{
    public sealed record IssuedRecord(long Id);
    public sealed record CouponMailRecord(string Code, string Kind, decimal Value, decimal MinSubtotal, decimal? MaxDiscount, DateTime ExpiresAt,
        string Subject, string Message, string Email, string Name);

    readonly string _storefrontUrl = (cfg["Storefront:PublicUrl"] ?? "http://localhost:3100").TrimEnd('/');

    public async Task HandleAsync(OutboxRepository.MessageRecord message, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize(message.Payload, OutboxJsonContext.Default.CampaignMessage)
            ?? throw new InvalidOperationException($"Outbox message {message.Id} has an empty payload");
        if (message.Type == OutboxTypes.CouponIssued) await MailAsync(payload.CouponId!.Value, ct);
        else await SendAsync(payload.CampaignId, ct);
    }

    async Task SendAsync(long campaignId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var campaign = await CouponStore.CampaignAsync(conn, null, campaignId);
        if (campaign is null || campaign.Status != "sending") return;

        List<long> customers;
        try
        {
            customers = await audience.CustomersInCohortAsync(campaign.CohortId, ct);
        }
        catch (AudienceUnavailableException ex)
        {
            await conn.ExecuteAsync("UPDATE campaigns SET status = 'failed', error = @Error WHERE id = @Id", new { Id = campaignId, Error = ex.Message });
            log.LogWarning("Campaign {Campaign} failed: {Message}", campaignId, ex.Message);
            return;
        }

        await using var tx = await conn.BeginTransactionAsync(ct);
        var locked = await CouponStore.CampaignAsync(conn, tx, campaignId);
        if (locked?.Status != "sending") return;
        var expires = clock.GetUtcNow().UtcDateTime.Date.AddDays(campaign.ValidDays + 1);
        await CouponStore.IssueAsync(conn, tx, new CouponStore.IssueRecord(campaignId, customers.ToArray(),
            customers.Select(_ => Coupon.NewCode("RS")).ToArray(), campaign.Kind, campaign.Value, campaign.MinSubtotal, campaign.MaxDiscount, expires));
        var issued = (await conn.QueryAsync<IssuedRecord>("SELECT id FROM coupons WHERE campaign_id = @Id ORDER BY id", new { Id = campaignId }, tx)).AsList();
        foreach (var coupon in issued)
            await OutboxWriter.EnqueueAsync(conn, tx, OutboxTypes.CouponIssued, new CampaignMessage(campaignId, coupon.Id), OutboxJsonContext.Default.CampaignMessage);
        await conn.ExecuteAsync("UPDATE campaigns SET status = 'sent', recipients = @Count, sent_at = now() WHERE id = @Id",
            new { Id = campaignId, Count = issued.Count }, tx);
        await tx.CommitAsync(ct);
        log.LogInformation("Campaign {Campaign}: {Count} coupons issued to cohort {Cohort}", campaignId, issued.Count, campaign.CohortId);
    }

    async Task MailAsync(long couponId, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var c = await conn.QuerySingleOrDefaultAsync<CouponMailRecord>("""
            SELECT k.code, k.kind, k.value, k.min_subtotal, k.max_discount, k.expires_at, c.subject, c.message, u.email, u.name
            FROM coupons k JOIN campaigns c ON c.id = k.campaign_id JOIN users u ON u.id = k.customer_id WHERE k.id = @Id
            """, new { Id = couponId });
        if (c is null) return;
        static string M(decimal v) => v.ToString("N2", CultureInfo.InvariantCulture);
        var offer = c.Kind == "percent" ? $"{c.Value:0.##}% off" : $"{M(c.Value)} TRY off";
        var terms = (c.MinSubtotal > 0 ? $"on orders of {M(c.MinSubtotal)} TRY or more" : "on your next order")
            + (c.MaxDiscount is { } cap ? $", up to {M(cap)} TRY" : "") + $". Valid until {c.ExpiresAt.AddDays(-1):d MMMM yyyy}.";
        var text = $"Hi {c.Name},\n\n{c.Message}\n\nYour personal code: {c.Code}\n{offer} {terms}\nIt works once and only with your account.\n\nShop now: {_storefrontUrl}\n";
        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:560px;margin:0 auto;color:#1f2330">
              <h1 style="color:#5b2bd6;font-size:22px">{WebUtility.HtmlEncode(c.Subject)}</h1>
              <p>Hi {WebUtility.HtmlEncode(c.Name)}, {WebUtility.HtmlEncode(c.Message)}</p>
              <p style="font-size:26px;font-weight:700;letter-spacing:2px;background:#efe9fd;color:#5b2bd6;padding:14px;text-align:center;border-radius:8px">{WebUtility.HtmlEncode(c.Code)}</p>
              <p><strong>{WebUtility.HtmlEncode(offer)}</strong> {WebUtility.HtmlEncode(terms)} It works once and only with your account.</p>
              <p><a href="{WebUtility.HtmlEncode(_storefrontUrl)}" style="display:inline-block;background:#5b2bd6;color:#fff;padding:10px 18px;border-radius:6px;text-decoration:none">Shop now</a></p>
            </div>
            """;
        await email.SendAsync(new EmailMessage(c.Email, c.Subject, text, html), ct);
    }
}
