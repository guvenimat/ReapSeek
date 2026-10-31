using Dapper;
using Npgsql;
using ReapSeek.Api.Infrastructure;
using ReapSeek.Shared.Outbox;

namespace ReapSeek.Api.Marketing;

public sealed class CampaignRepository(NpgsqlDataSource db)
{
    public sealed record CampaignInsertRecord(
        string Name, long CohortId, string CohortName, string Kind, decimal Value, decimal MinSubtotal, decimal? MaxDiscount, int ValidDays, string Subject, string Message);
    public sealed record CustomerCouponsRecord(long CustomerId, DateTime Now);
    public sealed record CampaignIdRecord(long Id);

    const string Select = """
        SELECT c.id, c.name, c.cohort_id, c.cohort_name, c.kind, c.value, c.min_subtotal, c.max_discount, c.valid_days, c.subject, c.message, c.status,
            c.recipients,
            (SELECT count(*)::int FROM coupons k JOIN orders o ON o.id = k.used_order_id WHERE k.campaign_id = c.id AND o.status = 'paid') AS redeemed,
            (SELECT coalesce(sum(o.total), 0) FROM coupons k JOIN orders o ON o.id = k.used_order_id WHERE k.campaign_id = c.id AND o.status = 'paid') AS revenue,
            c.error, c.created_at, c.sent_at
        FROM campaigns c
        """;

    public async Task<List<CustomerCoupon>> CustomerCouponsAsync(long customerId, DateTime now, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return (await conn.QueryAsync<CustomerCoupon>("""
            SELECT k.code, k.kind, k.value, k.min_subtotal, k.max_discount, k.expires_at, c.name AS campaign
            FROM coupons k LEFT JOIN campaigns c ON c.id = k.campaign_id
            WHERE k.customer_id = @CustomerId AND k.used_order_id IS NULL AND k.expires_at > @Now ORDER BY k.expires_at
            """, new CustomerCouponsRecord(customerId, now))).AsList();
    }

    public async Task<(int Reachable, int Known)> ReachAsync(long[] customerIds, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var reachable = await conn.ExecuteScalarAsync<int>("""
            SELECT count(*)::int FROM users WHERE id = ANY(@Ids) AND role = 'customer' AND email_verified_at IS NOT NULL AND suspended_at IS NULL
            """, new ReachRecord(customerIds));
        var known = await conn.ExecuteScalarAsync<int>("SELECT count(*)::int FROM users WHERE id = ANY(@Ids) AND role = 'customer'", new ReachRecord(customerIds));
        return (reachable, known);
    }

    public async Task<List<CampaignRow>> ListAsync(CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        return (await conn.QueryAsync<CampaignRow>(Select + " ORDER BY c.created_at DESC LIMIT 100")).AsList();
    }

    public async Task<CampaignRow> CreateAsync(CampaignInsertRecord campaign, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        var id = await conn.ExecuteScalarAsync<long>("""
            INSERT INTO campaigns (name, cohort_id, cohort_name, kind, value, min_subtotal, max_discount, valid_days, subject, message)
            VALUES (@Name, @CohortId, @CohortName, @Kind, @Value, @MinSubtotal, @MaxDiscount, @ValidDays, @Subject, @Message) RETURNING id
            """, campaign);
        return await conn.QuerySingleAsync<CampaignRow>(Select + " WHERE c.id = @Id", new CampaignIdRecord(id));
    }

    public async Task<CampaignRow> StartSendingAsync(long id, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var status = await conn.ExecuteScalarAsync<string?>("SELECT status FROM campaigns WHERE id = @Id FOR UPDATE", new CampaignIdRecord(id), tx)
            ?? throw new NotFoundException("Campaign not found");
        if (status is not ("draft" or "failed")) throw new ConflictException("This campaign was already sent", "campaign_sent");
        await conn.ExecuteAsync("UPDATE campaigns SET status = 'sending', error = NULL WHERE id = @Id", new CampaignIdRecord(id), tx);
        await OutboxWriter.EnqueueAsync(conn, tx, OutboxTypes.CampaignSend, new CampaignMessage(id, null), OutboxJsonContext.Default.CampaignMessage);
        await tx.CommitAsync(ct);
        return await conn.QuerySingleAsync<CampaignRow>(Select + " WHERE c.id = @Id", new CampaignIdRecord(id));
    }
}
