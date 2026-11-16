using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;
using ReapSeek.Domain.Marketing;
using ReapSeek.Shared.Analytics;
using ReapSeek.Modules.Marketing;
using ReapSeek.BuildingBlocks.Messaging;
using ReapSeek.Marketing.Contracts;

namespace ReapSeek.Marketing.Jobs;

internal sealed class CampaignHandler(NpgsqlDataSource db, ReapDeepAudience audience, TimeProvider clock, ILogger<CampaignHandler> log) : IInboxHandler<CampaignSendV1>
{
    public const string SendHandler = "campaign-send-issue-coupons";

    static readonly ModuleOutbox Outbox = new(MessagingModules.Marketing);

    public sealed record IssuedRecord(long Id);

    public Task HandleAsync(CampaignSendV1 message, InboxMessage envelope, CancellationToken ct) => SendAsync(message.CampaignId, ct);

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
            await Outbox.PublishAsync(conn, tx, CouponIssuedV1.EventName, new CouponIssuedV1(campaignId, coupon.Id), MarketingEventsJsonContext.Default.CouponIssuedV1);
        await conn.ExecuteAsync("UPDATE campaigns SET status = 'sent', recipients = @Count, sent_at = now() WHERE id = @Id",
            new { Id = campaignId, Count = issued.Count }, tx);
        await tx.CommitAsync(ct);
        log.LogInformation("Campaign {Campaign}: {Count} coupons issued to cohort {Cohort}", campaignId, issued.Count, campaign.CohortId);
    }
}
